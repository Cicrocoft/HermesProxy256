/*
 * Copyright (C) 2012-2020 CypherCore <http://github.com/CypherCore>
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Reflection;
using System.Collections.Concurrent;
using System.Threading;

using Framework.Constants;
using Framework.Cryptography;
using Framework.IO;
using Framework.Logging;
using Framework.Networking;
using HermesProxy.Configuration.Options;
using HermesProxy.Enums;
using Microsoft.Extensions.Options;
using Framework.Realm;

using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using static HermesProxy.World.Server.Packets.AuthResponse;
using System.Net;
using BNetServer;
using BNetServer.Services;
using Google.Protobuf;
using HermesProxy.World.Logging;

namespace HermesProxy.World.Server;

public partial class WorldSocket : SocketBase, BnetServices.INetwork
{
    static readonly int s_dumpLen =
        int.TryParse(System.Environment.GetEnvironmentVariable("HERMES_256_DUMPLEN"), out var dl) && dl > 0 ? dl : 128;

    // HERMES_256_QUIET=1 silences the [256-spike] send-path diagnostics: the per-packet PKT logging
    // (LogPacket, a disk write BEFORE the send - the handover's suspected drop mechanism) and the
    // "world packet out" hex dump. Tests whether the intermittent world-entry freeze is caused by
    // logging sitting in the send path rather than by a real protocol drop. Default off (logging on).
    static readonly bool s_quiet =
        System.Environment.GetEnvironmentVariable("HERMES_256_QUIET") == "1";

    // Source-generated [LoggerMessage] methods use this MEL logger. SourceFile and NetDir are
    // passed per-call but resolve to the same cached strings, so they compile to const loads.
    private static readonly Microsoft.Extensions.Logging.ILogger _melLog = Log.CreateMelLogger(Log.CategoryPacket);
    private static readonly string _sourceFile = nameof(WorldSocket).PadRight(15);
    private static readonly string _netDirRecv = Log.FormatDir(LogNetDir.C2P);
    private static readonly string _netDirSend = Log.FormatDir(LogNetDir.P2C);

    static readonly string ClientConnectionInitialize = "WORLD OF WARCRAFT CONNECTION - CLIENT TO SERVER - V2";
    static readonly string ServerConnectionInitialize = "WORLD OF WARCRAFT CONNECTION - SERVER TO CLIENT - V2";

    static readonly byte[] AuthCheckSeed = { 0xC5, 0xC6, 0x98, 0x95, 0x76, 0x3F, 0x1D, 0xCD, 0xB6, 0xA1, 0x37, 0x28, 0xB3, 0x12, 0xFF, 0x8A };
    static readonly byte[] SessionKeySeed = { 0x58, 0xCB, 0xCF, 0x40, 0xFE, 0x2E, 0xCE, 0xA6, 0x5A, 0x90, 0xB8, 0x01, 0x68, 0x6C, 0x28, 0x0B };
    static readonly byte[] ContinuedSessionSeed = { 0x16, 0xAD, 0x0C, 0xD4, 0x46, 0xF9, 0x4F, 0xB2, 0xEF, 0x7D, 0xEA, 0x2A, 0x17, 0x66, 0x4D, 0x2F };
    static readonly byte[] EncryptionKeySeed = { 0xE9, 0x75, 0x3C, 0x50, 0x90, 0x93, 0x61, 0xDA, 0x3B, 0x07, 0xEE, 0xFA, 0xFF, 0x9D, 0x41, 0xB8 };

    // The 5.5.0-generation engine replaced the whole key schedule: SHA-512 throughout, a 32-byte
    // encryption key, and these four seeds in place of the 16-byte ones above. Values from current
    // CypherCore, which tracks retail.
    static readonly byte[] AuthCheckSeed512 =
    {
        0xDE, 0x3A, 0x2A, 0x8E, 0x6B, 0x89, 0x52, 0x66, 0x88, 0x9D, 0x7E, 0x7A, 0x77, 0x1D, 0x5D, 0x1F,
        0x4E, 0xD9, 0x0C, 0x23, 0x9B, 0xCD, 0x0E, 0xDC, 0xD2, 0xE8, 0x04, 0x3A, 0x68, 0x64, 0xC7, 0xB0
    };
    static readonly byte[] SessionKeySeed512 =
    {
        0xE8, 0x1E, 0x8B, 0x59, 0x27, 0x62, 0x1E, 0xAA, 0x86, 0x15, 0x18, 0xEA, 0xC0, 0xBF, 0x66, 0x8C,
        0x6D, 0xBF, 0x83, 0x93, 0xBC, 0xAA, 0x80, 0x52, 0x5B, 0x1E, 0xDC, 0x23, 0xA0, 0x12, 0xB7, 0x50
    };
    static readonly byte[] ContinuedSessionSeed512 =
    {
        0x56, 0x5C, 0x61, 0x9C, 0x48, 0x3A, 0x52, 0x1F, 0x61, 0x5D, 0x05, 0x49, 0xB2, 0x9A, 0x39, 0xBF,
        0x4B, 0x97, 0xB0, 0x1B, 0xF9, 0x6C, 0xDE, 0xD6, 0x80, 0x1D, 0xAB, 0x26, 0x02, 0xA9, 0x9B, 0x9D
    };
    static readonly byte[] EncryptionKeySeed512 =
    {
        0x71, 0xC9, 0xED, 0x5A, 0xA7, 0x0E, 0x4D, 0xFF, 0x4C, 0x36, 0xA6, 0x5A, 0x3E, 0x46, 0x8A, 0x4A,
        0x5D, 0xA1, 0x48, 0xC8, 0x30, 0x47, 0x4A, 0xDE, 0xF6, 0x0D, 0x6C, 0xBE, 0x6F, 0xE4, 0x55, 0x73
    };

    static readonly int HeaderSize = 16;

    SocketBuffer _headerBuffer;
    SocketBuffer _packetBuffer;

    ConnectionType _connectType;
    ulong _key;

    byte[] _serverChallenge;
    WorldCrypt _worldCrypt;
    byte[] _sessionKey = null!;
    byte[] _encryptKey;
    ConnectToKey _instanceConnectKey;
    RealmId _realmId;

    // Per-session deflater for SMSG_COMPRESSED_PACKET. Both fields are touched only
    // under _sendLock (CompressPacket runs inside SendPacket's lock scope).
    private MemoryStream? _compressBuffer;
    private DeflateStream? _deflater;
    ConcurrentDictionary<Opcode, PacketHandler> _clientPacketTable = new();
    GlobalSessionData _globalSession = null!;
    readonly Lock _sendLock = new();

    private BnetServices.ServiceManager _bnetRpc = null!;

    private readonly string _externalAddress;
    private readonly int _instancePort;

    public WorldSocket(Socket socket, IOptions<ProxyNetworkOptions> networkOptions) : base(socket)
    {
        _externalAddress = networkOptions.Value.ExternalAddress;
        _instancePort = networkOptions.Value.InstancePort;

        _connectType = ConnectionType.Realm;
        // 16 bytes up to 3.4.3, 32 on the 5.5.0-generation engine — the same widening the client's
        // own LocalChallenge got. A short challenge leaves the client reading past the end of
        // SMSG_AUTH_CHALLENGE, and every HMAC below hashes this in full, so the length has to be
        // right in both places.
        _serverChallenge = Array.Empty<byte>().GenerateRandomKey(ModernVersion.Uses550Engine ? 32 : 16);
        _worldCrypt = new WorldCrypt();

        _encryptKey = new byte[ModernVersion.Uses550Engine ? 32 : 16];

        _headerBuffer = new SocketBuffer(HeaderSize);
        _packetBuffer = new SocketBuffer(0);

        InitializePacketHandlers();
    }

    public override void Dispose()
    {
        _serverChallenge = null!;
        _sessionKey = null!;
        _deflater?.Dispose();
        _deflater = null;
        _compressBuffer?.Dispose();
        _compressBuffer = null;

        base.Dispose();
    }

    public GlobalSessionData GetSession()
    {
        return _globalSession;
    }

    public GlobalSessionData Session => _globalSession;

    public override void Accept()
    {
        string ip_address = GetRemoteIpAddress()!.ToString();

        _packetBuffer.Resize(ClientConnectionInitialize.Length + 1);

        AsyncReadWithCallback(InitializeHandler);

        ByteBuffer packet = new();
        packet.WriteString(ServerConnectionInitialize);
        packet.WriteString("\n");
        AsyncWrite(packet.GetData());
    }

    void InitializeHandler(SocketAsyncEventArgs args)
    {
        if (args.SocketError != SocketError.Success)
        {
            CloseSocket();
            return;
        }

        if (args.BytesTransferred > 0)
        {
            if (_packetBuffer.GetRemainingSpace() > 0)
            {
                // need to receive the header
                int readHeaderSize = Math.Min(args.BytesTransferred, _packetBuffer.GetRemainingSpace());
                _packetBuffer.Write(args.Buffer!,0, readHeaderSize);

                if (_packetBuffer.GetRemainingSpace() > 0)
                {
                    // Couldn't receive the whole header this time.
                    AsyncReadWithCallback(InitializeHandler);
                    return;
                }

                ByteBuffer buffer = new(_packetBuffer.GetData());
                string initializer = buffer.ReadString((uint)ClientConnectionInitialize.Length);
                if (initializer != ClientConnectionInitialize)
                {
                    CloseSocket();
                    return;
                }

                byte terminator = buffer.ReadUInt8();
                if (terminator != '\n')
                {
                    CloseSocket();
                    return;
                }

                // Per-session deflater. Raw-deflate (DeflateStream is no-header, matching
                // the legacy windowBits=-15 setting). CompressionLevel.Fastest maps to
                // zlib level 1 in zlib-ng on .NET 10. leaveOpen=true keeps the backing
                // MemoryStream alive across packets so Flush() can drain a single
                // sync-flushed packet at a time.
                _compressBuffer = new MemoryStream();
                _deflater = new DeflateStream(_compressBuffer, CompressionLevel.Fastest, leaveOpen: true);

                _packetBuffer.Resize(0);
                _packetBuffer.Reset();
                HandleSendAuthSession();
                AsyncRead();
                return;
            }
        }
    }

    public override void ReadHandler(SocketAsyncEventArgs args)
    {
        if (!IsOpen())
            return;

        int currentReadIndex = 0;
        while (currentReadIndex < args.BytesTransferred)
        {
            if (_headerBuffer.GetRemainingSpace() > 0)
            {
                // need to receive the header
                int readHeaderSize = Math.Min(args.BytesTransferred - currentReadIndex, _headerBuffer.GetRemainingSpace());
                _headerBuffer.Write(args.Buffer!,currentReadIndex, readHeaderSize);
                currentReadIndex += readHeaderSize;

                if (_headerBuffer.GetRemainingSpace() > 0)
                    break; // Couldn't receive the whole header this time.

                // We just received nice new header
                if (!ReadHeader())
                {
                    CloseSocket();
                    return;
                }
            }

            // We have full read header, now check the data payload
            if (_packetBuffer.GetRemainingSpace() > 0)
            {
                // need more data in the payload
                int readDataSize = Math.Min(args.BytesTransferred - currentReadIndex, _packetBuffer.GetRemainingSpace());
                _packetBuffer.Write(args.Buffer!,currentReadIndex, readDataSize);
                currentReadIndex += readDataSize;

                if (_packetBuffer.GetRemainingSpace() > 0)
                    break; // Couldn't receive the whole data this time.
            }

            // just received fresh new payload
            ReadDataHandlerResult result = ReadData();
            _headerBuffer.Reset();
            if (result != ReadDataHandlerResult.Ok)
            {
                CloseSocket();
                return;
            }
        }

        AsyncRead();
    }

    bool ReadHeader()
    {
        PacketHeader header = new();
        header.Read(_headerBuffer.GetData());

        _packetBuffer.Resize(header.Size);
        return true;
    }

    ReadDataHandlerResult ReadData()
    {
        PacketHeader header = new();
        header.Read(_headerBuffer.GetData());

        byte[] payloadData = _packetBuffer.GetData();

        if (!_worldCrypt.Decrypt(payloadData, header.Tag))
        {
            Log.Print(LogType.Error, $"WorldSocket.ReadData(): client {GetRemoteIpAddress()} failed to decrypt packet (size: {header.Size})");
            return ReadDataHandlerResult.Error;
        }

        // FIXME(256-spike): temporary diagnostics. The 5.5.0-engine client's opcodes do not look
        // like anything in the table we borrowed, so dump the raw bytes to work out the actual
        // on-the-wire encoding rather than guessing at field widths. Remove before any PR.
        {
            byte[] raw = _packetBuffer.GetData();
            int dumpLength = System.Math.Min(raw.Length, 256);
            Log.Print(LogType.Warn,
                $"[256-spike] world packet in: declaredSize={header.Size} payloadLen={raw.Length} " +
                $"tag={System.Convert.ToHexString(header.Tag)} first{dumpLength}={System.Convert.ToHexString(raw, 0, dumpLength)}");
        }

        WorldPacket packet = new(_packetBuffer.GetData());
        _packetBuffer.Reset();

        Opcode opcode = packet.GetUniversalOpcode(true);

        WorldSocketLogMessages.PacketReceived(_melLog, _sourceFile, _netDirRecv, opcode, packet.GetOpcode());

        if (opcode != Opcode.CMSG_HOTFIX_REQUEST && !header.IsValidSize())
        {
            Log.Print(LogType.Error, $"WorldSocket.ReadHeaderHandler(): client {GetRemoteIpAddress()} sent malformed packet (size: {header.Size})");
            return ReadDataHandlerResult.Error;
        }

        switch (opcode)
        {
            case Opcode.CMSG_PING:
                Ping ping = new(packet);
                ping.Read();
                if (_connectType == ConnectionType.Realm && GetSession().WorldClient != null && GetSession().WorldClient!.IsConnected() && GetSession().WorldClient!.IsAuthenticated())
                    GetSession().WorldClient!.SendPing(ping.Serial, ping.Latency);
                else
                    HandlePing(ping);
                break;
            case Opcode.CMSG_AUTH_SESSION:
                AuthSession authSession = new(packet);
                authSession.Read();
                HandleAuthSession(authSession);
                break;
            case Opcode.CMSG_AUTH_CONTINUED_SESSION:
                AuthContinuedSession authContinuedSession = new(packet);
                authContinuedSession.Read();
                HandleAuthContinuedSession(authContinuedSession);
                break;
            case Opcode.CMSG_KEEP_ALIVE:
                break;
            case Opcode.CMSG_LOG_DISCONNECT:
                uint reason = packet.ReadUInt32();
                Log.Print(LogType.Server, $"Client disconnected with reason {reason}.");
                // A client that gives up during the world handshake sends this before it has a
                // session, and the code below assumed one existed. Previously unreachable because
                // the opcode did not decode; now that it does, an early disconnect would take the
                // whole proxy down with a NullReferenceException.
                if (GetSession() == null)
                {
                    CloseSocket();
                    break;
                }
                if (_connectType == ConnectionType.Realm)
                {
                    if (GetSession().AuthClient != null)
                        GetSession().AuthClient.Disconnect();
                    if (GetSession().WorldClient != null)
                        GetSession().WorldClient!.Disconnect();
                }
                if (GetSession().ModernSniff != null)
                {
                    GetSession().ModernSniff!.CloseFile();
                    GetSession().ModernSniff = null!;
                }

                break;
            case Opcode.CMSG_ENABLE_NAGLE:
                SetNoDelay(false);
                GetSession()?.WorldClient?.SetNoDelay(false);
                break;
            case Opcode.CMSG_CONNECT_TO_FAILED:
                ConnectToFailed connectToFailed = new(packet);
                connectToFailed.Read();
                HandleConnectToFailed(connectToFailed);
                break;
            case Opcode.CMSG_ENTER_ENCRYPTED_MODE_ACK:
                HandleEnterEncryptedModeAck();
                break;
            case Opcode.CMSG_SERVER_TIME_OFFSET_REQUEST:
                SendServerTimeOffset();
                break;
            default:
                HandlePacket(packet);
                break;
        }

        return ReadDataHandlerResult.Ok;
    }

    public void HandlePacket(WorldPacket packet)
    {
        Opcode universalOpcode = packet.GetUniversalOpcode(isModern: true);
        var handler = GetHandler(universalOpcode);
        if (handler != null)
        {
            // A handler that reads past the end of a packet must not take the session with it.
            // That happens whenever an opcode in a per-build table points at the wrong message:
            // the reader runs out of bytes, throws, and the exception propagates out of the socket
            // read loop and drops the connection. Log it and carry on — one unknown packet is not
            // worth a disconnect, and on a build whose opcode table is still being verified it is
            // the difference between playing and being kicked.
            try
            {
                if (HermesProxy.Server.MetricsEnabled)
                {
                    long startTimestamp = Stopwatch.GetTimestamp();
                    handler.Invoke(this, packet);
                    HermesProxy.Server.Metrics.RecordClientToServerLatency(universalOpcode, Stopwatch.GetElapsedTime(startTimestamp).Ticks);
                }
                else
                {
                    handler.Invoke(this, packet);
                }
            }
            catch (Exception ex)
            {
                Log.Print(LogType.Error,
                    $"Handler for {universalOpcode} (0x{packet.GetOpcode():X}) threw {ex.GetType().Name}: {ex.Message}. " +
                    "Packet dropped; session kept alive.");
            }
        }
        else
            WorldSocketLogMessages.NoHandlerForOpcode(_melLog, _sourceFile, _netDirRecv, universalOpcode, packet.GetOpcode());
    }

    private void SendPacketToServer(WorldPacket packet, Opcode delayUntilOpcode = Opcode.MSG_NULL_ACTION)
    {
        if (GetSession().WorldClient != null)
            GetSession().WorldClient!.SendPacketToServer(packet, delayUntilOpcode);
        else
            Log.Print(LogType.Error, $"Attempt to send opcode {packet.GetUniversalOpcode(false)} ({packet.GetOpcode()}) while WorldClient is disconnected!");
    }

    public PacketHandler? GetHandler(Opcode opcode)
    {
        return _clientPacketTable.LookupByKey(opcode);
    }

    // C<P S: Sends data to modern client
    // FIXME(256-spike): the override exists only for the encrypted-mode opcode probe below.
    // Remove along with the probe.
    /// <summary>
    /// BISECT: combat and spell-visual packets held back on the 5.5.0 engine.
    ///
    /// These fire during login — the legacy core sends a login visual and the proxy forwards it —
    /// and every one of them is a dense bit-packed structure whose 11.x layout has never been
    /// checked. None is needed to stay connected. Removing whole objects made things worse, but a
    /// malformed packet is a different failure from a missing one.
    /// </summary>
    public void SendPacket(ServerPacket packet, uint wireOpcodeOverride = 0)
    {

        if (!IsOpen())
        {
            Log.PrintNet(LogType.Error, LogNetDir.P2C, $"Can't send {packet.GetUniversalOpcode()}, socket is closed!");
            if (GetSession() != null)
            {
                if (GetSession().RealmSocket == this)
                    GetSession().RealmSocket = null!;
                else if (GetSession().InstanceSocket == this)
                    GetSession().InstanceSocket = null!;
                GetSession().OnDisconnect();
            }
            return;
        }

        packet.WritePacketData();
        if (GetSession() != null && !s_quiet)
            packet.LogPacket(ref GetSession().ModernSniff, GetSession().PacketLogContext);

        lock (_sendLock)
        {
            var data = packet.GetData()!;
            Opcode universalOpcode = packet.GetUniversalOpcode();

            // 2.5.6 runs on the 5.5.0-generation engine, whose opcodes are (group << 16) | index
            // and travel as 32 bits. Older builds keep the 16-bit form.
            bool wideOpcode = ModernVersion.Build == ClientVersionBuild.V2_5_6_69110;
            uint opcode = wireOpcodeOverride != 0 ? wireOpcodeOverride : packet.GetOpcode();

            // An opcode of 0 means the build's table has no number for this message. 2.5.6's table
            // is derived from the client binary and is deliberately incomplete: it only carries
            // messages the client actually has, so retail-only packets resolve to nothing here.
            // Putting a 0 on the wire would make the client treat it as an unknown message and
            // drop the connection, which is far harder to diagnose than skipping the send.
            // Opcode 0 is never valid on the wire, so drop the packet whatever its universal
            // opcode says. The earlier form of this guard also required the universal opcode to be
            // something other than MSG_NULL_ACTION, which let a 688-byte SMSG_AVAILABLE_HOTFIXES go
            // out as opcode 0 — the client read an unknown message with a large body in the middle
            // of the glue-screen sequence.
            if (opcode == 0)
            {
                Log.Print(LogType.Debug,
                    $"WorldSocket.SendPacket: {universalOpcode} has no opcode in build " +
                    $"{ModernVersion.Build} ({data.Length} byte body); not sent.");
                return;
            }

            // Under-sending is what crashes this client: its reader runs off the end of our body and
            // the packed-guid assembler dereferences a null buffer (REFERENCE-256-CLIENT.md 118).
            // These four are the ones we provably send shorter than the client's own reader consumes
            // and that go out during a normal session. Over-sending is harmless and is not listed.
            if (ModernVersion.Uses550Engine && !s_noSendGuard && s_underSized.Contains(universalOpcode))
            {
                Log.Print(LogType.Warn,
                    $"[256-spike] holding back {universalOpcode}: our body is shorter than the " +
                    $"client's reader for that opcode. See REFERENCE-256-CLIENT.md section 118.");
                return;
            }

            WorldSocketLogMessages.PacketSent(_melLog, _sourceFile, _netDirSend, universalOpcode, (uint)opcode);

            // FIXME(256-spike): temporary diagnostics, mirroring the inbound dump above, so the
            // bytes the client actually receives can be compared against a known-good build.
            // Remove before any PR.
            if (!s_quiet)
            {
                // HERMES_256_DUMPLEN raises the cap so a whole create block can be decoded
                // against our own field map. 128 bytes stops inside the movement block and
                // never reaches UnitData, which is where every remaining fault lives.
                int dumpLength = System.Math.Min(data.Length, s_dumpLen);
                Log.Print(LogType.Warn,
                    $"[256-spike] world packet out: {universalOpcode} opcode=0x{opcode:X} " +
                    $"bodyLen={data.Length} first{dumpLength}={System.Convert.ToHexString(data, 0, dumpLength)}");
            }

            ByteBuffer buffer = new();

            int packetSize = data.Length;
            if (packetSize > 0x400 && _worldCrypt.IsInitialized)
            {
                buffer.WriteInt32(packetSize + (wideOpcode ? 4 : 2));
                Span<byte> opcodeBytes = stackalloc byte[4];
                if (wideOpcode)
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(opcodeBytes, opcode);
                else
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(opcodeBytes, (ushort)opcode);
                opcodeBytes = opcodeBytes[..(wideOpcode ? 4 : 2)];
                buffer.WriteUInt32(Adler32.Update(Adler32.Update(0x9827D8F1, opcodeBytes), data.AsSpan(0, packetSize)));

                byte[] compressedData;
                uint compressedSize = CompressPacket(data, opcode, wideOpcode, out compressedData);
                buffer.WriteUInt32(Adler32.Update(0x9827D8F1, compressedData.AsSpan(0, (int)compressedSize)));
                buffer.WriteBytes(compressedData, compressedSize);

                packetSize = (int)(compressedSize + 12);
                opcode = ModernVersion.GetCurrentOpcode(Opcode.SMSG_COMPRESSED_PACKET);
                System.Diagnostics.Trace.Assert(opcode != 0);

                data = buffer.GetData();
            }

            buffer = new ByteBuffer();
            if (wideOpcode)
                buffer.WriteUInt32(opcode);
            else
                buffer.WriteUInt16((ushort)opcode);
            buffer.WriteBytes(data);
            packetSize += wideOpcode ? 4 : 2 /*opcode*/;

            data = buffer.GetData();

            PacketHeader header = new();
            header.Size = packetSize;
            _worldCrypt.Encrypt(data, header.Tag);

            ByteBuffer byteBuffer = new();
            header.Write(byteBuffer);
            byteBuffer.WriteBytes(data);

            AsyncWrite(byteBuffer.GetData());
        }
    }

    public uint CompressPacket(byte[] data, uint opcode, bool wideOpcode, out byte[] outData)
    {
        // Drain the prior packet's output, then push opcode + body and flush. Flush()
        // on a compress-mode DeflateStream emits a Z_SYNC_FLUSH boundary (00 00 FF FF)
        // on .NET 6+, matching the legacy deflate(strm, Z_SYNC_FLUSH) call. The deflater
        // state (sliding window / huffman tables) persists across calls because we keep
        // reusing the same instance.
        _compressBuffer!.SetLength(0);
        _compressBuffer.Position = 0;

        Span<byte> hdr = stackalloc byte[4];
        if (wideOpcode)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hdr, opcode);
        else
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(hdr, (ushort)opcode);
        _deflater!.Write(hdr[..(wideOpcode ? 4 : 2)]);
        _deflater.Write(data);
        _deflater.Flush();

        uint produced = (uint)_compressBuffer.Length;
        outData = _compressBuffer.GetBuffer();
        return produced;
    }

    public override bool Update()
    {
        if (!base.Update())
            return false;

        return true;
    }

    public override void OnClose()
    {
        base.OnClose();
    }

    void HandleSendAuthSession()
    {
        AuthChallenge challenge = new();
        challenge.Challenge = _serverChallenge;
        challenge.DosChallenge = new byte[32].GenerateRandomKey(32);
        challenge.DosZeroBits = 1;

        SendPacket(challenge);
    }

    void HandleAuthSession(AuthSession authSession)
    {
        // A ticket the storage cannot resolve used to throw KeyNotFoundException here, which took
        // down the whole proxy rather than just this connection.
        if (!BnetSessionTicketStorage.TryGetSessionByJoinTicket(authSession.RealmJoinTicket, out var resolvedSession))
        {
            Log.Print(LogType.Error,
                $"WorldSocket.HandleAuthSession: no session for realm join ticket " +
                $"'{authSession.RealmJoinTicket}' (length {authSession.RealmJoinTicket.Length}). " +
                $"Known tickets: [{string.Join(", ", BnetSessionTicketStorage.SessionsByName.Keys)}]");
            CloseSocket();
            return;
        }

        _globalSession = resolvedSession!;
        _bnetRpc = new BnetServices.ServiceManager("WorldSocket", this, _globalSession);
        HandleAuthSessionCallback(authSession);
    }

    void HandleAuthSessionCallback(AuthSession authSession)
    {
        RealmBuildInfo? buildInfo = GetSession().RealmManager.GetBuildInfo(GetSession().Build);
        if (buildInfo == null)
        {
            SendAuthResponseError(BattlenetRpcErrorCode.BadVersion);
            Log.Print(LogType.Error, $"WorldSocket.HandleAuthSessionCallback: Missing auth seed for realm build {GetSession().Build} ({GetRemoteIpAddress()}).");
            CloseSocket();
            GetSession().OnDisconnect();
            return;
        }

        // For hook purposes, we get Remoteaddress at this point.
        var address = GetRemoteIpAddress();

        // FIXME(256-spike): temporary diagnostics. Remove before any PR. Prints key material for
        // the same reason as the capture in AuthenticationV2 — these are the four inputs to the
        // client's Digest, and with them the build auth key can be recovered offline by searching
        // the client binary. Recovering it turns Digest into an oracle that decides whether
        // keyData is built correctly, without having to guess and re-login.
        Log.Print(LogType.Warn,
            $"[256-spike] CAPTURE keyData={System.Convert.ToHexString(GetSession().SessionKey)}");
        Log.Print(LogType.Warn,
            $"[256-spike] CAPTURE serverChallenge={System.Convert.ToHexString(_serverChallenge)}");
        Log.Print(LogType.Warn,
            $"[256-spike] CAPTURE localChallenge={System.Convert.ToHexString(authSession.LocalChallenge)}");
        Log.Print(LogType.Warn,
            $"[256-spike] CAPTURE digest={System.Convert.ToHexString(authSession.Digest)}");

        bool TrySeed(byte[] seed)
        {
            byte[] digest;
            if (ModernVersion.Uses550Engine)
            {
                Sha512 digestKeyHash = new();
                digestKeyHash.Process(GetSession().SessionKey, GetSession().SessionKey.Length);
                digestKeyHash.Finish(seed);
                HmacSha512 hmac = new(digestKeyHash.Digest!);
                hmac.Process(authSession.LocalChallenge, authSession.LocalChallenge.Length);
                hmac.Process(_serverChallenge, _serverChallenge.Length);
                hmac.Finish(AuthCheckSeed512, AuthCheckSeed512.Length);
                digest = hmac.Digest!;
            }
            else
            {
                Sha256 digestKeyHash = new();
                digestKeyHash.Process(GetSession().SessionKey, GetSession().SessionKey.Length);
                digestKeyHash.Finish(seed);
                HmacSha256 hmac = new(digestKeyHash.Digest!);
                hmac.Process(authSession.LocalChallenge, authSession.LocalChallenge.Length);
                hmac.Process(_serverChallenge, _serverChallenge.Length);
                hmac.Finish(AuthCheckSeed, 16);
                digest = hmac.Digest!;
            }

            // The client truncates its digest to 24 bytes; compare only what it sent.
            return digest.AsSpan(0, authSession.Digest.Length).SequenceEqual(authSession.Digest);
        }

        if (GetSession().OS != "Wn64" && GetSession().OS != "Mc64" && GetSession().OS != "MacA" /*TODO what is windows arm?*/)
        {
            Log.Print(LogType.Error, $"WorldSocket.HandleAuthSession: Unknown OS for account: {GetSession().GameAccountInfo.Id} ('{authSession.RealmJoinTicket}') address: {address}");
            CloseSocket();
            GetSession().OnDisconnect();
            return;
        }
        
        byte[]? platformSeed = buildInfo.BuildSeeds.GetValueOrDefault(GetSession().OS);
        if (platformSeed == null || !TrySeed(platformSeed))
        {
            Log.Print(LogType.Debug, $"WorldSocket.HandleAuthSession: Fallback to static seed");
            if (!TrySeed(buildInfo.FallbackStaticSeed))
            {
                // The TrySeed check proves the client's binary matches a known Blizzard build
                // (the seed is embedded in the client .exe). Seeds are only known for builds
                // with community captures; V3_4_3_54261 doesn't have a published seed yet.
                // In an emulation/proxy context the check protects nothing — the subsequent
                // encryption-key derivation (below) doesn't depend on buildInfo.FallbackStaticSeed,
                // only on the SRP6 session key + challenges which both sides already agree on.
                // So for 3.4.3+ we warn-and-continue; older builds stay strict because their
                // seeds are known and a mismatch would indicate a real config error.
                if (ModernVersion.Build >= ClientVersionBuild.V3_4_3_54261)
                {
                    Log.Print(LogType.Warn, $"WorldSocket.HandleAuthSession: Seed check failed for account: {GetSession().GameAccountInfo.Id} ('{authSession.RealmJoinTicket}') address: {address} — bypassing (unknown per-build seed for {ModernVersion.Build}).");
                }
                else
                {
                    Log.Print(LogType.Error, $"WorldSocket.HandleAuthSession: Authentication failed for account: {GetSession().GameAccountInfo.Id} ('{authSession.RealmJoinTicket}') address: {address}");
                    CloseSocket();
                    GetSession().OnDisconnect();
                    return;
                }
            }
        }

        // FIXME(256-spike): decisive keyData probe. The digest check above is bypassed for lack of a
        // per-build auth key, so we never actually confirm our keyData matches the client's. But
        // encryptKey — the value the client verifies our signature over — depends ONLY on keyData +
        // the two challenges, never on the build key. So if this build appends an EMPTY per-build key
        // (i.e. digest keyed on SHA512(keyData) alone), the client's digest will match on emptyKey
        // below, proving keyData is correct and pointing the ack failure at the signing itself rather
        // than the key. A miss on every variant points back at keyData (handoff H2).
        if (ModernVersion.Uses550Engine)
        {
            // Recompute the client's digest for an arbitrary keyData candidate with an EMPTY per-build
            // key, and test both challenge orderings. A match identifies the true world keyData.
            bool TryKeyData(byte[] keyData, bool localFirst)
            {
                Sha512 dk = new();
                dk.Process(keyData, keyData.Length);
                dk.Finish(Array.Empty<byte>());
                HmacSha512 h = new(dk.Digest!);
                if (localFirst)
                {
                    h.Process(authSession.LocalChallenge, authSession.LocalChallenge.Length);
                    h.Process(_serverChallenge, _serverChallenge.Length);
                }
                else
                {
                    h.Process(_serverChallenge, _serverChallenge.Length);
                    h.Process(authSession.LocalChallenge, authSession.LocalChallenge.Length);
                }
                h.Finish(AuthCheckSeed512, AuthCheckSeed512.Length);
                return h.Digest!.AsSpan(0, authSession.Digest.Length).SequenceEqual(authSession.Digest);
            }

            byte[] joinKey = GetSession().BnetSessionKeyFromJoin ?? Array.Empty<byte>();
            Log.Print(LogType.Warn,
                $"[256-spike] digest probe: emptyKey={TrySeed(Array.Empty<byte>())}, " +
                $"platformSeed={(platformSeed != null && TrySeed(platformSeed))}, " +
                $"fallback={TrySeed(buildInfo.FallbackStaticSeed)}");
            Log.Print(LogType.Warn,
                $"[256-spike] keyData candidates vs clientDigest: " +
                $"secretPair(LF)={TryKeyData(GetSession().SessionKey, true)}, " +
                $"secretPair(SF)={TryKeyData(GetSession().SessionKey, false)}, " +
                $"bnetKey(LF)={(joinKey.Length > 0 && TryKeyData(joinKey, true))}, " +
                $"bnetKey(SF)={(joinKey.Length > 0 && TryKeyData(joinKey, false))}");

            // Raw input dump for offline brute-force (local private server, debug only).
            Log.Print(LogType.Warn,
                $"[256-spike] RAW keyData={Convert.ToHexString(GetSession().SessionKey)} " +
                $"bnetKey={Convert.ToHexString(joinKey)} " +
                $"serverChallenge={Convert.ToHexString(_serverChallenge)} " +
                $"localChallenge={Convert.ToHexString(authSession.LocalChallenge)} " +
                $"clientDigest={Convert.ToHexString(authSession.Digest)}");
        }

        // The 5.5.0-generation engine runs the same schedule on SHA-512, seeded differently, and
        // ends with a 32-byte AES key instead of 16.
        _sessionKey = new byte[40];
        byte[] encryptKeyDigest;
        if (ModernVersion.Uses550Engine)
        {
            Sha512 keyData = new();
            keyData.Finish(GetSession().SessionKey);

            HmacSha512 sessionKeyHmac = new(keyData.Digest!);
            sessionKeyHmac.Process(_serverChallenge, _serverChallenge.Length);
            sessionKeyHmac.Process(authSession.LocalChallenge, authSession.LocalChallenge.Length);
            sessionKeyHmac.Finish(SessionKeySeed512, SessionKeySeed512.Length);

            new SessionKeyGenerator(sessionKeyHmac.Digest!, sessionKeyHmac.Digest!.Length, sha512: true)
                .Generate(_sessionKey, 40);

            HmacSha512 encryptKeyGen = new(_sessionKey);
            encryptKeyGen.Process(authSession.LocalChallenge, authSession.LocalChallenge.Length);
            encryptKeyGen.Process(_serverChallenge, _serverChallenge.Length);
            encryptKeyGen.Finish(EncryptionKeySeed512, EncryptionKeySeed512.Length);
            encryptKeyDigest = encryptKeyGen.Digest!;
        }
        else
        {
            Sha256 keyData = new();
            keyData.Finish(GetSession().SessionKey);

            HmacSha256 sessionKeyHmac = new(keyData.Digest!);
            sessionKeyHmac.Process(_serverChallenge, _serverChallenge.Length);
            sessionKeyHmac.Process(authSession.LocalChallenge, authSession.LocalChallenge.Length);
            sessionKeyHmac.Finish(SessionKeySeed, 16);

            new SessionKeyGenerator(sessionKeyHmac.Digest!, 32).Generate(_sessionKey, 40);

            HmacSha256 encryptKeyGen = new(_sessionKey);
            encryptKeyGen.Process(authSession.LocalChallenge, authSession.LocalChallenge.Length);
            encryptKeyGen.Process(_serverChallenge, _serverChallenge.Length);
            encryptKeyGen.Finish(EncryptionKeySeed, 16);
            encryptKeyDigest = encryptKeyGen.Digest!;
        }

        // Only the leading bytes of the hmac are used.
        Buffer.BlockCopy(encryptKeyDigest, 0, _encryptKey, 0, _encryptKey.Length);

        // FIXME(256-spike): dump encryptKey so the Ed25519 signature over it can be verified offline
        // against the client's baked-in public key. Confirms our signing is internally consistent;
        // if it verifies, the ack failure is a keyData/model mismatch, not a signing bug.
        if (ModernVersion.Uses550Engine)
            Log.Print(LogType.Warn, $"[256-spike] encryptKey={Convert.ToHexString(_encryptKey)}");

        // FIXME(256-spike): temporary diagnostics. Remove before any PR. Lengths and a zero-check
        // only — never the key material. The client verifies a signature over the encryption key,
        // so a wrong length or an all-zero input here explains a silent refusal to ack.
        {
            byte[] keyData = GetSession().SessionKey;
            bool keyDataZero = true;
            foreach (byte b in keyData)
                if (b != 0) { keyDataZero = false; break; }
            Log.Print(LogType.Warn,
                $"[256-spike] key schedule: keyData={keyData.Length} bytes (allZero={keyDataZero}), " +
                $"serverChallenge={_serverChallenge.Length}, localChallenge={authSession.LocalChallenge.Length}, " +
                $"digest={authSession.Digest.Length}, sessionKey={_sessionKey.Length}, encryptKey={_encryptKey.Length}");
        }

        GetSession().SessionKey = _sessionKey;

        Log.Print(LogType.Server, $"WorldSocket:HandleAuthSession: Client '{authSession.RealmJoinTicket}' authenticated successfully from {address}.");

        _realmId = new RealmId((byte)authSession.RegionID, (byte)authSession.BattlegroupID, authSession.RealmID);
        GetSession().WorldClient = new Client.WorldClient();
        if (!GetSession().WorldClient!.ConnectToWorldServer(GetSession().RealmManager.GetRealm(_realmId)!, GetSession()))
        {
            SendAuthResponseError(BattlenetRpcErrorCode.BadServer);
            Log.Print(LogType.Error, "The WorldClient failed to connect to the selected world server!");
            Session.AccountMetaDataMgr.InvalidateLastSelectedCharacter();
            CloseSocket();
            GetSession().OnDisconnect();
            return;
        }

        // FIXME(256-spike): temporary probe. Remove before any PR.
        //
        // The client resolves RegionGroup against an internal table before it will even check the
        // signature, and a miss is silent — it keeps pinging and never acknowledges. Which ids are
        // valid is not knowable from the binary (the table is built at runtime), so walk a range
        // and let the client tell us: the ack stops the walk, and the last value sent is the
        // answer. Safe to send repeatedly precisely because a miss is a no-op for the client.
        // FIXME(256-spike): temporary probe. Remove before any PR.
        //
        // 0x4D0003 for SMSG_ENTER_ENCRYPTED_MODE is derived from 12.0.0's table, not measured. The
        // client's own handler table puts the encrypted-mode handler seven slots from the
        // SMSG_AUTH_CHALLENGE handler, and AUTH_CHALLENGE is confirmed to be index 0 — so index 3
        // is unlikely to be right. A wrong opcode looks exactly like everything else we have seen:
        // the client neither answers nor disconnects. Walk the group and let it tell us.
        // FIXME(256-spike): temporary probe. Remove before any PR.
        //
        // Opcodes 0x4D0000-3 leave the connection alive while 4-7 drop it, which reads as "the
        // client knows these four and rejects the rest". Silence on an opcode it knows points at
        // the packet being discarded before dispatch — most likely on size, since 69 bytes is the
        // only body we have ever sent. So walk opcode x layout instead: the surviving opcodes let
        // several variants go out in a single session.
        string? matrix = Environment.GetEnvironmentVariable("HERMES_ENC_MATRIX");
        if (!string.IsNullOrEmpty(matrix))
        {
            var wires = new System.Collections.Generic.List<uint>();
            foreach (var part in matrix.Split(','))
                wires.Add(System.Convert.ToUInt32(part.Trim(), 16));
            string[] layouts = { "before", "after", "none" };
            int region = int.TryParse(Environment.GetEnvironmentVariable("HERMES_ENC_REGION"), out var mr) ? mr : 0;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                foreach (uint wire in wires)
                {
                    foreach (string layout in layouts)
                    {
                        if (_encryptedModeAcked) return;
                        Log.Print(LogType.Warn,
                            $"[256-spike] probing opcode 0x{wire:X6} layout={layout} " +
                            $"({(layout == "none" ? 65 : 69)} bytes)");
                        SendPacket(new EnterEncryptedMode(_encryptKey, true, region, layout), wire);
                        await System.Threading.Tasks.Task.Delay(900);
                    }
                }

                if (!_encryptedModeAcked)
                    Log.Print(LogType.Warn, "[256-spike] matrix finished with no acknowledgement");
            });
            return;
        }

        string? opcodeSweep = Environment.GetEnvironmentVariable("HERMES_ENC_OPCODE_SWEEP");
        if (!string.IsNullOrEmpty(opcodeSweep))
        {
            // A comma-separated list, not a range: one opcode in this group is already known to
            // close the socket, which would end the walk before the untested ones are reached.
            var candidates = new System.Collections.Generic.List<uint>();
            foreach (var part in opcodeSweep.Split(','))
                candidates.Add(System.Convert.ToUInt32(part.Trim(), 16));
            int region = int.TryParse(Environment.GetEnvironmentVariable("HERMES_ENC_REGION"), out var rg) ? rg : 0;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                foreach (uint wire in candidates)
                {
                    if (_encryptedModeAcked) break;
                    Log.Print(LogType.Warn, $"[256-spike] probing opcode 0x{wire:X6} (RegionGroup={region})");
                    SendPacket(new EnterEncryptedMode(_encryptKey, true, region), wire);
                    await System.Threading.Tasks.Task.Delay(900);
                }

                if (!_encryptedModeAcked)
                    Log.Print(LogType.Warn, "[256-spike] opcode sweep finished with no acknowledgement");
            });
            return;
        }

        string? sweep = Environment.GetEnvironmentVariable("HERMES_ENC_REGION_SWEEP");
        if (!string.IsNullOrEmpty(sweep))
        {
            var bounds = sweep.Split('-');
            int from = int.Parse(bounds[0]);
            int to = bounds.Length > 1 ? int.Parse(bounds[1]) : from;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                for (int region = from; region <= to && !_encryptedModeAcked; region++)
                {
                    // The opcode is the other unknown, and the two are coupled: the region lookup
                    // happens inside the handler, so probing one while the other is wrong can never
                    // succeed. Pin the opcode here so this sweep varies exactly one thing.
                    uint wire = uint.TryParse(Environment.GetEnvironmentVariable("HERMES_ENC_OPCODE"),
                        System.Globalization.NumberStyles.HexNumber, null, out var w) ? w : 0;
                    Log.Print(LogType.Warn, $"[256-spike] probing RegionGroup={region} (opcode 0x{(wire != 0 ? wire : 0x4D0003):X6})");
                    SendPacket(new EnterEncryptedMode(_encryptKey, true, region), wire);
                    await System.Threading.Tasks.Task.Delay(900);
                }

                if (!_encryptedModeAcked)
                    Log.Print(LogType.Warn, "[256-spike] region sweep finished with no acknowledgement");
            });
            return;
        }

        int single = int.TryParse(Environment.GetEnvironmentVariable("HERMES_ENC_REGION"), out var r) ? r : 0;

        // FIXME(256-spike): temporary. Remove before any PR.
        //
        // The client verifies the signature, then — only when Enabled is set — makes a virtual call
        // and disconnects with reason 3 if it returns 1. With Enabled clear it skips that check
        // entirely and falls through to the normal continuation, so this tells us whether anything
        // past the signature is still wrong.
        bool enabled = Environment.GetEnvironmentVariable("HERMES_ENC_ENABLED") != "0";
        SendPacket(new EnterEncryptedMode(_encryptKey, enabled, single));
    }

    public struct ConnectToKey
    {
        public ulong Raw
        {
            get { return ((ulong)AccountId | ((ulong)connectionType << 32) | (Key << 33)); }
            set
            {
                AccountId = (uint)(value & 0xFFFFFFFF);
                connectionType = (ConnectionType)((value >> 32) & 1);
                Key = (value >> 33);
            }
        }

        public uint AccountId;
        public ConnectionType connectionType;
        public ulong Key;
    }

    void HandleAuthContinuedSession(AuthContinuedSession authSession)
    {
        ConnectToKey key = new();
        _key = key.Raw = authSession.Key;

        _connectType = key.connectionType;
        if (_connectType != ConnectionType.Instance)
        {
            SendAuthResponseError(BattlenetRpcErrorCode.Denied);
            CloseSocket();
            return;
        }

        HandleAuthContinuedSessionCallback(authSession);
    }

    void HandleAuthContinuedSessionCallback(AuthContinuedSession authSession)
    {
        ConnectToKey key = new();
        _key = key.Raw = authSession.Key;

        _globalSession = BnetSessionTicketStorage.SessionsByKey[_key];

        uint accountId = key.AccountId;
        string login = GetSession().AccountInfo.Login;
        _sessionKey = GetSession().SessionKey;

        byte[] continuedDigest;
        if (ModernVersion.Uses550Engine)
        {
            HmacSha512 hmac512 = new(_sessionKey);
            hmac512.Process(BitConverter.GetBytes(authSession.Key), 8);
            hmac512.Process(authSession.LocalChallenge, authSession.LocalChallenge.Length);
            hmac512.Process(_serverChallenge, _serverChallenge.Length);
            hmac512.Finish(ContinuedSessionSeed512, ContinuedSessionSeed512.Length);
            continuedDigest = hmac512.Digest!;
        }
        else
        {
            HmacSha256 hmac = new(_sessionKey);
            hmac.Process(BitConverter.GetBytes(authSession.Key), 8);
            hmac.Process(authSession.LocalChallenge, authSession.LocalChallenge.Length);
            hmac.Process(_serverChallenge, _serverChallenge.Length);
            hmac.Finish(ContinuedSessionSeed, 16);
            continuedDigest = hmac.Digest!;
        }

        if (!continuedDigest.AsSpan(0, authSession.Digest.Length).SequenceEqual(authSession.Digest))
        {
            Log.Print(LogType.Error, $"WorldSocket.HandleAuthContinuedSession: Authentication failed for account: {accountId} ('{login}') address: {GetRemoteIpAddress()}");
            CloseSocket();
            return;
        }

        byte[] continuedKeyDigest;
        if (ModernVersion.Uses550Engine)
        {
            HmacSha512 encryptKeyGen512 = new(_sessionKey);
            encryptKeyGen512.Process(authSession.LocalChallenge, authSession.LocalChallenge.Length);
            encryptKeyGen512.Process(_serverChallenge, _serverChallenge.Length);
            encryptKeyGen512.Finish(EncryptionKeySeed512, EncryptionKeySeed512.Length);
            continuedKeyDigest = encryptKeyGen512.Digest!;
        }
        else
        {
            HmacSha256 encryptKeyGen = new(_sessionKey);
            encryptKeyGen.Process(authSession.LocalChallenge, authSession.LocalChallenge.Length);
            encryptKeyGen.Process(_serverChallenge, _serverChallenge.Length);
            encryptKeyGen.Finish(EncryptionKeySeed, 16);
            continuedKeyDigest = encryptKeyGen.Digest!;
        }

        // Only the leading bytes of the hmac are used.
        Buffer.BlockCopy(continuedKeyDigest, 0, _encryptKey, 0, _encryptKey.Length);

        SendPacket(new EnterEncryptedMode(_encryptKey, true));
    }

    public void SendConnectToInstance(ConnectToSerial serial)
    {
        IPAddress externalIp = IPAddress.Parse(_externalAddress);
        IPEndPoint instanceAddress = new IPEndPoint(externalIp, _instancePort);
        
        _instanceConnectKey.AccountId = GetSession().AccountInfo.Id;
        _instanceConnectKey.connectionType = ConnectionType.Instance;
        _instanceConnectKey.Key = RandomHelper.URand(0, 0x7FFFFFFF);

        BnetSessionTicketStorage.AddNewSessionByKey(_instanceConnectKey.Raw, GetSession());

        ConnectTo connectTo = new();
        connectTo.Key = _instanceConnectKey.Raw;
        connectTo.Serial = serial;
        connectTo.Payload.Port = (ushort)_instancePort;
        connectTo.Con = (byte)ConnectionType.Instance;
        connectTo.NativeRealmAddress = GetSession().RealmId.GetAddress();

        if (instanceAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            connectTo.Payload.Where.IPv4 = instanceAddress.Address.GetAddressBytes();
            connectTo.Payload.Where.Type = ConnectTo.AddressType.IPv4;
        }
        else
        {
            connectTo.Payload.Where.IPv6 = instanceAddress.Address.GetAddressBytes();
            connectTo.Payload.Where.Type = ConnectTo.AddressType.IPv6;
        }

        SendPacket(connectTo);
    }
    public class CharacterLoginFailed : ServerPacket
    {
        public CharacterLoginFailed(LoginFailureReason code) : base(Opcode.SMSG_CHARACTER_LOGIN_FAILED)
        {
            Code = code;
        }

        public override void Write()
        {
            _worldPacket.WriteUInt8((byte)Code);
        }

        LoginFailureReason Code;
    }
    public void AbortLogin(LoginFailureReason reason)
    {
        SendPacket(new CharacterLoginFailed(reason));

    }
    void HandleConnectToFailed(ConnectToFailed connectToFailed)
    {
        switch (connectToFailed.Serial)
        {
            case ConnectToSerial.WorldAttempt1:
                SendConnectToInstance(ConnectToSerial.WorldAttempt2);
                break;
            case ConnectToSerial.WorldAttempt2:
                SendConnectToInstance(ConnectToSerial.WorldAttempt3);
                break;
            case ConnectToSerial.WorldAttempt3:
                SendConnectToInstance(ConnectToSerial.WorldAttempt4);
                break;
            case ConnectToSerial.WorldAttempt4:
                SendConnectToInstance(ConnectToSerial.WorldAttempt5);
                break;
            case ConnectToSerial.WorldAttempt5:
            {
                Log.Print(LogType.Error, "Failed to connect 5 times to world socket, aborting login");
                AbortLogin(LoginFailureReason.NoWorld);
                break;
            }
            default:
                return;
        }
    }

    volatile bool _encryptedModeAcked;

    void HandleEnterEncryptedModeAck()
    {
        // FIXME(256-spike): temporary, paired with the region probe above. Remove before any PR.
        _encryptedModeAcked = true;
        Log.Print(LogType.Warn, "[256-spike] client acknowledged encrypted mode");

        // FIXME(256-spike): log the world key at the UNIVERSAL init point, not just HandleAuthSession.
        // The instance/continued-session socket (8086, where the create batch flows) derives its key
        // on a path that skips the AuthSession log, so its traffic could not be decrypted offline.
        // Every world socket reaches here. Remove before any PR (prints key material).
        Log.Print(LogType.Warn,
            $"[256-spike] worldkey connType={_connectType} encryptKey={System.Convert.ToHexString(_encryptKey)}");

        _worldCrypt.Initialize(_encryptKey);
        if (_connectType == ConnectionType.Realm)
        {
            SendAuthResponse(BattlenetRpcErrorCode.Ok, GetSession().WorldClient!.GetQueuePosition());

            // FIXME(256-spike): the glue-screen extras are suppressed on the 5.5.0 generation until
            // each one's body is verified against that generation's layout. They are optional —
            // they carry store/feature flags and a cache version, none of which the character list
            // depends on — but several of them contain array counts, and a count read from the
            // wrong offset makes the client allocate against a garbage length and abort. Send only
            // what character select actually needs, then reinstate them one at a time.
            // FIXME(256-spike): the glue-screen feature block is off again. Bisected: with it the
            // client dies on a null dereference, without it the character list arrives intact and
            // the screen merely stays black — and an empty character list crashes identically, so
            // the per-character record is not involved. Its 5.5.0 body still has a wrong field
            // somewhere among ~44 gated bits.
            //
            // The two trivial ones are back on: a lone uint32 and a lone byte cannot carry a
            // mis-sized length, so they are safe to test and may be what the glue screen was
            // actually waiting for.
            // Every glue-screen extra tried so far — the feature block, the cache version, the
            // Battle.net connection state — produces the same null dereference at RVA 0x2DF25DA,
            // while sending none of them does not. The last two are a bare uint32 and a bare byte,
            // so their bodies cannot be at fault: their opcodes are derived, never verified, and a
            // wrong one lands the message in a handler that expects something else.
            // The glue-screen feature block is sent again. It crashed the client before, but that
            // was the wrong opcode (0x460063 is the in-game variant on this build) combined with a
            // guessed body; both now come from the client's own parser. The character list arrives
            // intact without it and the screen still stays black, which is what a client that never
            // received its glue-screen configuration looks like.
            SendFeatureSystemStatusGlueScreen();

            // Without this the client's in-game Battle.net link never comes up. Its own glue log
            // shows "Connected to Back", then exactly 30 seconds later "Session with Battle.net
            // destroyed" followed by "Disconnected from WoW" — which is the disconnect we kept
            // seeing, and it is unrelated to the world socket.
            SendBnetConnectionState(1);

            // The client stages its startup around hotfixes ("Initial Hotfixes: Requested /
            // Received / Applied" in its own strings) and CypherCore sends this too. An empty list
            // is a valid answer and lets that stage complete.
            SendAvailableHotfixes();

            // ...but an empty list also means the client never sends CMSG_HOTFIX_REQUEST, so it
            // never receives hotfix data, never runs "ApplyingHotfixes from Cache", and never logs
            // "Done applying initial hotfixes". That last step is what advances the client's state
            // machine to 3, which is the only thing that flips the two flags the glue callback at
            // RVA 0x1EFAE0 tests before it will hand the character list to the UI. Sending an empty
            // hotfix set unprompted closes that loop.
            SendEmptyHotfixConnect();

            // Next in CypherCore's glue sequence, and its opcode and body are now both read from
            // the client rather than derived.
            SendSetTimeZoneInformation();

            if (!ModernVersion.Uses550Engine)
            {
                SendClientCacheVersion(0);
            }
            GetSession().AccountDataMgr = new AccountDataManager(GetSession().Username, GetSession().RealmManager.GetRealm(_realmId)!.Name);
            GetSession().RealmSocket = this;

            // CypherCore sends this at the glue screen, before any character is chosen, and the
            // client has a "Store Account Data request %d finished" log line right next to its glue
            // code. We only ever sent it reactively, when the legacy core happened to.
            if (ModernVersion.Uses550Engine)
                SendAccountDataTimes();

            // Flush any Realm-destined packets the legacy WorldClient queued before this
            // socket was ready. See WorldClient.SendPacketToClientDirect for the producer.
            var gameState = GetSession().GameState;
            if (gameState.PendingRealmPackets.Count > 0)
            {
                lock (gameState.PendingRealmPacketsLock)
                {
                    while (gameState.PendingRealmPackets.TryDequeue(out var queuedPacket))
                        SendPacket(queuedPacket);
                }
            }
        }
        else
        {
            Log.Print(LogType.Server, "Client has connected to the instance server.");
            SendPacket(new ResumeComms(ConnectionType.Instance));
            GetSession().InstanceSocket = this;
        }
    }

    public void SendAuthResponseError(BattlenetRpcErrorCode code)
    {
        AuthResponse response = new();
        response.SuccessInfo = null!;
        response.WaitInfo = null!;
        response.Result = code;
        SendPacket(response);
    }

    public void SendAuthResponse(BattlenetRpcErrorCode code, uint queuePos = 0)
    {
        Log.Print(LogType.Trace,
            $"[Trace] SendAuthResponse: code={code} queuePos={queuePos} legacyExpansion={LegacyVersion.ExpansionVersion} " +
            $"realmAddress=0x{_realmId.GetAddress():X8}");
        AuthResponse response = new();
        response.Result = code;

        if (code == BattlenetRpcErrorCode.Ok)
        {
            response.SuccessInfo = new AuthResponse.AuthSuccessInfo();
            response.SuccessInfo.ActiveExpansionLevel = (byte)LegacyVersion.ExpansionVersion;
            response.SuccessInfo.AccountExpansionLevel = (byte)LegacyVersion.ExpansionVersion;
            response.SuccessInfo.VirtualRealmAddress = _realmId.GetAddress();
            response.SuccessInfo.Time = (uint)Time.UnixTime;

            var realm = GetSession().RealmManager.GetRealm(_realmId)!;

            // Send current home realm. Also there is no need to send it later in realm queries.
            response.SuccessInfo!.VirtualRealms.Add(new VirtualRealmInfo(realm.Id.GetAddress(), true, false, realm.Name, realm.NormalizedName));

            List<RaceClassAvailability> availableRaces = new List<RaceClassAvailability>();
            RaceClassAvailability race = new RaceClassAvailability();

            race.RaceID = 1;
            race.Classes.Add(new ClassAvailability(1, 0, 0));
            race.Classes.Add(new ClassAvailability(2, 0, 0));
            race.Classes.Add(new ClassAvailability(4, 0, 0));
            race.Classes.Add(new ClassAvailability(5, 0, 0));
            race.Classes.Add(new ClassAvailability(8, 0, 0));
            race.Classes.Add(new ClassAvailability(9, 0, 0));
            availableRaces.Add(race);

            race = new RaceClassAvailability();
            race.RaceID = 2;
            race.Classes.Add(new ClassAvailability(1, 0, 0));
            race.Classes.Add(new ClassAvailability(3, 0, 0));
            race.Classes.Add(new ClassAvailability(4, 0, 0));
            race.Classes.Add(new ClassAvailability(7, 0, 0));
            race.Classes.Add(new ClassAvailability(9, 0, 0));
            availableRaces.Add(race);

            race = new RaceClassAvailability();
            race.RaceID = 3;
            race.Classes.Add(new ClassAvailability(1, 0, 0));
            race.Classes.Add(new ClassAvailability(2, 0, 0));
            race.Classes.Add(new ClassAvailability(3, 0, 0));
            race.Classes.Add(new ClassAvailability(5, 0, 0));
            race.Classes.Add(new ClassAvailability(4, 0, 0));
            availableRaces.Add(race);

            race = new RaceClassAvailability();
            race.RaceID = 4;
            race.Classes.Add(new ClassAvailability(1, 0, 0));
            race.Classes.Add(new ClassAvailability(3, 0, 0));
            race.Classes.Add(new ClassAvailability(4, 0, 0));
            race.Classes.Add(new ClassAvailability(5, 0, 0));
            race.Classes.Add(new ClassAvailability(11, 0, 0));
            availableRaces.Add(race);

            race = new RaceClassAvailability();
            race.RaceID = 5;
            race.Classes.Add(new ClassAvailability(1, 0, 0));
            race.Classes.Add(new ClassAvailability(4, 0, 0));
            race.Classes.Add(new ClassAvailability(5, 0, 0));
            race.Classes.Add(new ClassAvailability(8, 0, 0));
            race.Classes.Add(new ClassAvailability(9, 0, 0));
            availableRaces.Add(race);

            race = new RaceClassAvailability();
            race.RaceID = 6;
            race.Classes.Add(new ClassAvailability(1, 0, 0));
            race.Classes.Add(new ClassAvailability(3, 0, 0));
            race.Classes.Add(new ClassAvailability(7, 0, 0));
            race.Classes.Add(new ClassAvailability(11, 0, 0));
            availableRaces.Add(race);

            race = new RaceClassAvailability();
            race.RaceID = 7;
            race.Classes.Add(new ClassAvailability(1, 0, 0));
            race.Classes.Add(new ClassAvailability(4, 0, 0));
            race.Classes.Add(new ClassAvailability(8, 0, 0));
            race.Classes.Add(new ClassAvailability(9, 0, 0));
            availableRaces.Add(race);

            race = new RaceClassAvailability();
            race.RaceID = 8;
            race.Classes.Add(new ClassAvailability(1, 0, 0));
            race.Classes.Add(new ClassAvailability(4, 0, 0));
            race.Classes.Add(new ClassAvailability(3, 0, 0));
            race.Classes.Add(new ClassAvailability(5, 0, 0));
            race.Classes.Add(new ClassAvailability(7, 0, 0));
            race.Classes.Add(new ClassAvailability(8, 0, 0));
            availableRaces.Add(race);

            if (ModernVersion.ExpansionVersion >= 2 &&
                LegacyVersion.ExpansionVersion >= 2)
            {
                race = new RaceClassAvailability();
                race.RaceID = 10;
                race.Classes.Add(new ClassAvailability(3, 0, 0));
                race.Classes.Add(new ClassAvailability(4, 0, 0));
                race.Classes.Add(new ClassAvailability(5, 0, 0));
                race.Classes.Add(new ClassAvailability(8, 0, 0));
                race.Classes.Add(new ClassAvailability(9, 0, 0));
                race.Classes.Add(new ClassAvailability(2, 0, 0));
                availableRaces.Add(race);

                race = new RaceClassAvailability();
                race.RaceID = 11;
                race.Classes.Add(new ClassAvailability(1, 0, 0));
                race.Classes.Add(new ClassAvailability(2, 0, 0));
                race.Classes.Add(new ClassAvailability(3, 0, 0));
                race.Classes.Add(new ClassAvailability(5, 0, 0));
                race.Classes.Add(new ClassAvailability(8, 0, 0));
                race.Classes.Add(new ClassAvailability(7, 0, 0));
                availableRaces.Add(race);
            }

            response.SuccessInfo.AvailableClasses = availableRaces;
        }

        if (queuePos != 0)
        {
            response.WaitInfo = new AuthWaitInfo();
            response.WaitInfo.WaitCount = queuePos;
        }

        SendPacket(response);
    }

    public void SendAuthWaitQue(uint position)
    {
        if (position != 0)
        {
            WaitQueueUpdate waitQueueUpdate = new();
            waitQueueUpdate.WaitInfo.WaitCount = position;
            waitQueueUpdate.WaitInfo.WaitTime = 0;
            waitQueueUpdate.WaitInfo.HasFCM = false;
            SendPacket(waitQueueUpdate);
        }
        else
            SendPacket(new WaitQueueFinish());
    }

    public void SendSetTimeZoneInformation()
    {
        // @todo: replace dummy values
        SetTimeZoneInformation packet = new();
        packet.ServerTimeTZ = "Europe/Paris";
        packet.GameTimeTZ = "Europe/Paris";
        packet.ServerRegionalTimeTZ = "Europe/Paris";

        SendPacket(packet);//enabled it
    }

    public void SendFeatureSystemStatusGlueScreen()
    {
        FeatureSystemStatusGlueScreen features = new();
        features.BpayStoreAvailable = false;
        features.BpayStoreDisabledByParentalControls = false;
        features.CharUndeleteEnabled = false;
        features.BpayStoreEnabled = false;
        features.MaxCharactersPerRealm = 10;
        features.MinimumExpansionLevel = 5;
        features.MaximumExpansionLevel = 8;
        features.Unk14 = true;

        var europaTicketConfig = new EuropaTicketConfig();
        europaTicketConfig.ThrottleState.MaxTries = 10;
        europaTicketConfig.ThrottleState.PerMilliseconds = 60000;
        europaTicketConfig.ThrottleState.TryCount = 1;
        europaTicketConfig.ThrottleState.LastResetTimeBeforeNow = 111111;
        europaTicketConfig.TicketsEnabled = true;
        europaTicketConfig.BugsEnabled = true;
        europaTicketConfig.ComplaintsEnabled = true;
        europaTicketConfig.SuggestionsEnabled = true;

        features.EuropaTicketSystemStatus = europaTicketConfig;

        SendPacket(features);
    }

    /// <summary>
    /// Messages whose body we build shorter than this build's client reads for that opcode. Each is
    /// a crash waiting to happen, and two of them already fired today. Holding them back loses the
    /// feature and keeps the session. HERMES_256_NOGUARD=1 sends them anyway.
    /// </summary>
    static readonly System.Collections.Frozen.FrozenSet<Opcode> s_underSized =
        System.Collections.Frozen.FrozenSet.ToFrozenSet(new[]
        {
            // Released once their numbers were corrected: SMSG_ATTACK_START (0x4C001B) and
            // SMSG_AI_REACTION (0x460163) both over-send at the right slot, and over-sending cannot
            // fault. They were never too short - they were pointed at the wrong reader.
            // Gate rates this FATAL: a packed guid at token 29 cannot complete against the captured
            // body. The capture is from the old writer at the old opcode, so it is very likely a
            // stale row - but the criterion is the one that has predicted every crash so far, and
            // this fires 153x a session. Remove once one session's capture clears it.
            Opcode.SMSG_SPELL_START,
            Opcode.SMSG_MAIL_QUERY_NEXT_TIME_RESULT,   // number known (0x460205) but the body is 8
                                                       // bytes against a 26-byte minimum; move both
                                                       // together or the gate stays red
            Opcode.SMSG_SET_DUNGEON_DIFFICULTY,        // no known correct number; 5.5.0's +15 slot
                                                       // reads u8,u8,string and this is one int32
        });

    static readonly bool s_noSendGuard =
        System.Environment.GetEnvironmentVariable("HERMES_256_NOGUARD") == "1";

    public void SendFeatureSystemStatus()
    {
        // The 5.5.0 body in FeatureSystemStatus.Write550 is now derived from the client binary
        // itself (reader at RVA 0x5A7830, both derivations in REFERENCE-256-CLIENT.md sections
        // 89/106 agree), not from WowPacketParser - the parser-transcribed bodies are what
        // crashed the client three times. It goes out by default; HERMES_256_NOFEATURES=1
        // restores the suppression if it misbehaves. A successful test: unset the variable,
        // log in with the 2.5.6 client, and enter the world - the client must neither crash
        // during loading (wrong size -> null deref in the packed-guid assembler at RVA
        // 0x2DF25DA) nor hang or drop features visibly worse than with the packet withheld.
        if (ModernVersion.Uses550Engine
            && System.Environment.GetEnvironmentVariable("HERMES_256_NOFEATURES") == "1")
        {
            return;
        }

        FeatureSystemStatus features = new();
        features.ComplaintStatus = 2;
        features.ScrollOfResurrectionRequestsRemaining = 1;
        features.ScrollOfResurrectionMaxRequestsPerDay = 1;
        features.CfgRealmID = 1;
        features.CfgRealmRecID = 1;
        features.TwitterPostThrottleLimit = 60;
        features.TwitterPostThrottleCooldown = 20;
        features.TokenPollTimeSeconds = 300;
        features.KioskSessionMinutes = 30;
        features.BpayStoreProductDeliveryDelay = 180;
        features.HiddenUIClubsPresenceUpdateTimer = 60000;
        features.VoiceEnabled = false;
        features.BrowserEnabled = false;

        features.EuropaTicketSystemStatus = new EuropaTicketConfig();
        features.EuropaTicketSystemStatus.ThrottleState.MaxTries = 10;
        features.EuropaTicketSystemStatus.ThrottleState.PerMilliseconds = 60000;
        features.EuropaTicketSystemStatus.ThrottleState.TryCount = 1;
        features.EuropaTicketSystemStatus.ThrottleState.LastResetTimeBeforeNow = 111111;

        features.TutorialsEnabled = true;
        features.Unk67 = true;
        features.QuestSessionEnabled = true;
        features.BattlegroundsEnabled = true;

        features.QuickJoinConfig.ToastDuration = 7;
        features.QuickJoinConfig.DelayDuration = 10;
        features.QuickJoinConfig.QueueMultiplier = 1;
        features.QuickJoinConfig.PlayerMultiplier = 1;
        features.QuickJoinConfig.PlayerFriendValue = 5;
        features.QuickJoinConfig.PlayerGuildValue = 1;
        features.QuickJoinConfig.ThrottleDecayTime = 60;
        features.QuickJoinConfig.ThrottlePrioritySpike = 20;
        features.QuickJoinConfig.ThrottlePvPPriorityNormal = 50;
        features.QuickJoinConfig.ThrottlePvPPriorityLow = 1;
        features.QuickJoinConfig.ThrottlePvPHonorThreshold = 10;
        features.QuickJoinConfig.ThrottleLfgListPriorityDefault = 50;
        features.QuickJoinConfig.ThrottleLfgListPriorityAbove = 100;
        features.QuickJoinConfig.ThrottleLfgListPriorityBelow = 50;
        features.QuickJoinConfig.ThrottleLfgListIlvlScalingAbove = 1;
        features.QuickJoinConfig.ThrottleLfgListIlvlScalingBelow = 1;
        features.QuickJoinConfig.ThrottleRfPriorityAbove = 100;
        features.QuickJoinConfig.ThrottleRfIlvlScalingAbove = 1;
        features.QuickJoinConfig.ThrottleDfMaxItemLevel = 850;
        features.QuickJoinConfig.ThrottleDfBestPriority = 80;

        features.Squelch.IsSquelched = false;
        features.Squelch.BnetAccountGuid = WowGuid128.Create(HighGuidType703.BNetAccount, GetSession().AccountInfo.Id);
        features.Squelch.GuildGuid = WowGuid128.Empty;

        features.EuropaTicketSystemStatus.TicketsEnabled = true;
        features.EuropaTicketSystemStatus.BugsEnabled = true;
        features.EuropaTicketSystemStatus.ComplaintsEnabled = true;
        features.EuropaTicketSystemStatus.SuggestionsEnabled = true;

        features.EuropaTicketSystemStatus.ThrottleState.MaxTries = 10;
        features.EuropaTicketSystemStatus.ThrottleState.PerMilliseconds = 60000;
        features.EuropaTicketSystemStatus.ThrottleState.TryCount = 1;
        features.EuropaTicketSystemStatus.ThrottleState.LastResetTimeBeforeNow = 10627480;
        SendPacket(features);
    }

    public void SendSeasonInfo()
    {
        SeasonInfo seasonInfo = new();
        if (LegacyVersion.ExpansionVersion > 1 &&
            ModernVersion.ExpansionVersion > 1)
        {
            seasonInfo.CurrentSeason = 2;
            seasonInfo.PreviousSeason = 1;
        }
        SendPacket(seasonInfo);
    }

    public void SendMotd()
    {
        MOTD motd = new();
        SendPacket(motd);
    }

    public void SendClientCacheVersion(uint version)
    {
        ClientCacheVersion cache = new();
        cache.CacheVersion = version;
        SendPacket(cache);
    }

    /// <summary>
    /// Sends SMSG_HOTFIX_CONNECT with no records, to complete the client's initial hotfix stage
    /// even though there is nothing to apply. Normally this is only sent in reply to
    /// CMSG_HOTFIX_REQUEST, which the client never sends when the available-hotfix list is empty.
    /// </summary>
    public void SendEmptyHotfixConnect()
    {
        SendPacket(new HotfixConnect());
    }

    public void SendAvailableHotfixes()
    {
        AvailableHotfixes hotfixes = new AvailableHotfixes();
        hotfixes.VirtualRealmAddress = GetSession().RealmId.GetAddress();
        // V3_4_3 ships ~700k real WotLK hotfix records; enumerating them all in
        // SMSG_AVAILABLE_HOTFIXES yields a multi-MB packet that stalls the client
        // at the glue-screen loading bar (character preview never renders). Suppress
        // the record list and let the client lazy-fetch via CMSG_DB_QUERY_BULK /
        // CMSG_HOTFIX_REQUEST — both paths return real data via HotfixHandler.
        if (ModernVersion.Build == ClientVersionBuild.V3_4_3_54261)
            hotfixes.IncludeRecords = false;
        SendPacket(hotfixes);
    }

    public void SendBnetConnectionState(byte state)
    {
        ConnectionStatus bnetConnected = new();
        bnetConnected.State = state;
        SendPacket(bnetConnected);
    }

    public void SendServerTimeOffset()
    {
        ServerTimeOffset response = new();
        response.Time = Time.UnixTime;
        SendPacket(response);
    }

    void HandlePing(Ping ping)
    {
        SendPacket(new Pong(ping.Serial));
    }

    public void SendAccountDataTimes()
    {
        System.Diagnostics.Trace.Assert(_connectType == ConnectionType.Realm);

        // At the glue screen no character is selected yet, so this runs with an empty guid —
        // which is what CypherCore sends there too.
        WowGuid128 guid = GetSession().GameState.CurrentPlayerGuid;
        GetSession().AccountDataMgr.LoadAllData(guid);

        AccountDataTimes accountData = new AccountDataTimes();
        accountData.PlayerGuid = guid;
        accountData.ServerTime = Time.UnixTime;

        int count = ModernVersion.GetAccountDataCount();
        accountData.AccountTimes = new long[count];
        for (int i = 0; i < count; i++)
            accountData.AccountTimes[i] = GetSession().AccountDataMgr.Data[i] != null ? GetSession().AccountDataMgr.Data[i].Timestamp : 0;

        SendPacket(accountData);
    }

    public void SendRpcMessage(uint serviceId, OriginalHash service, uint methodId, uint token, BattlenetRpcErrorCode status, IMessage? message)
    {
        var methodInfo = new MethodCall();
        methodInfo.SetServiceHash((uint)service);
        methodInfo.SetMethodId(methodId);
        methodInfo.Token = token;
        methodInfo.ObjectId = serviceId;

        byte[] bytes = message == null ? Array.Empty<byte>() : message.ToByteArray();
        BattlenetResponse response = new()
        {
            Method = methodInfo,
            Status = status,
            Data   = new ByteBuffer(bytes),
        };

        SendPacket(response);
    }

    public IPEndPoint GetRemoteIpEndPoint()
    {
        return GetRemoteIpAddress()!;
    }

    public void InitializePacketHandlers()
    {
        foreach (var methodInfo in typeof(WorldSocket).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            foreach (var msgAttr in methodInfo.GetCustomAttributes<PacketHandlerAttribute>())
            {
                if (msgAttr == null)
                    continue;

                if (msgAttr.Opcode == Opcode.MSG_NULL_ACTION)
                    continue;

                if (_clientPacketTable.ContainsKey(msgAttr.Opcode))
                {
                    Log.Print(LogType.Error, $"Tried to override OpcodeHandler of {_clientPacketTable[msgAttr.Opcode].ToString()} with {methodInfo.Name} (Opcode {msgAttr.Opcode})");
                    continue;
                }

                var parameters = methodInfo.GetParameters();
                if (parameters.Length == 0)
                {
                    Log.Print(LogType.Error, $"Method: {methodInfo.Name} Has no paramters");
                    continue;
                }

                if (parameters[0].ParameterType.BaseType != typeof(ClientPacket))
                {
                    Log.Print(LogType.Error, $"Method: {methodInfo.Name} has wrong BaseType");
                    continue;
                }

                _clientPacketTable[msgAttr.Opcode] = new PacketHandler(methodInfo, parameters[0].ParameterType);
            }
        }
    }

    public class PacketHandler
    {
        public PacketHandler(MethodInfo info, Type type)
        {
            methodCaller = (Action<WorldSocket, ClientPacket>)GetType().GetMethod("CreateDelegate", BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(type).Invoke(null, new object[] { info })!;
            packetType = type;
        }

        public void Invoke(WorldSocket session, WorldPacket packet)
        {
            if (packetType == null)
                return;

            using var clientPacket = (ClientPacket)Activator.CreateInstance(packetType, packet)!;
            clientPacket.LogPacket(ref session.GetSession().ModernSniff, session.GetSession().PacketLogContext);
            clientPacket.Read();
            methodCaller(session, clientPacket);
        }

        static Action<WorldSocket, ClientPacket> CreateDelegate<P1>(MethodInfo method) where P1 : ClientPacket
        {
            // create first delegate. It is not fine because its 
            // signature contains unknown types T and P1
            Action<WorldSocket, P1> d = (Action<WorldSocket, P1>)method.CreateDelegate(typeof(Action<WorldSocket, P1>));
            // create another delegate having necessary signature. 
            // It encapsulates first delegate with a closure
            return delegate (WorldSocket target, ClientPacket p) { d(target, (P1)p); };
        }

        Action<WorldSocket, ClientPacket> methodCaller = null!;
        Type packetType;
    }
}

enum ReadDataHandlerResult
{
    Ok = 0,
    Error = 1
}
