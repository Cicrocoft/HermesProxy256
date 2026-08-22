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

using Framework.Constants;
using Framework.Cryptography;
using Framework.IO;
using HermesProxy.World.Enums;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Linq;

namespace HermesProxy.World.Server.Packets;

class Ping : ClientPacket
{
    public Ping(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        Serial = _worldPacket.ReadUInt32();
        Latency = _worldPacket.ReadUInt32();
    }

    public uint Serial;
    public uint Latency;
}

class Pong : ServerPacket, ISpanWritable
{
    public Pong(uint serial) : base(Opcode.SMSG_PONG)
    {
        Serial = serial;
    }

    public override void Write()
    {
        _worldPacket.WriteUInt32(Serial);
    }

    public int MaxSize => 4; // uint

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WriteUInt32(Serial);
        return writer.Position;
    }

    uint Serial;
}

class AuthChallenge : ServerPacket, ISpanWritable
{
    public AuthChallenge() : base(Opcode.SMSG_AUTH_CHALLENGE) { }

    public override void Write()
    {
        _worldPacket.WriteBytes(DosChallenge);
        _worldPacket.WriteBytes(Challenge);
        _worldPacket.WriteUInt8(DosZeroBits);
    }

    // DosChallenge(32) + Challenge(16 or 32) + byte(1)
    public int MaxSize => 32 + 32 + 1;

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WriteBytes(DosChallenge);
        writer.WriteBytes(Challenge);
        writer.WriteUInt8(DosZeroBits);
        return writer.Position;
    }

    public byte[] Challenge = new byte[16];
    public byte[] DosChallenge = new byte[32]; // Encryption seeds
    public byte DosZeroBits;
}

class AuthSession : ClientPacket
{
    public AuthSession(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        DosResponse = _worldPacket.ReadUInt64();
        RegionID = _worldPacket.ReadUInt32();
        BattlegroupID = _worldPacket.ReadUInt32();
        RealmID = _worldPacket.ReadUInt32();

        // The challenge is 16 bytes up to 3.4.3 and 32 on the modern engine. Reading only 16 from a
        // 2.5.6 client leaves the rest of the body shifted, so the join ticket comes out empty —
        // and the half-challenge would then be fed into the session and encryption key derivation,
        // which both hash it in full.
        LocalChallenge = _worldPacket.ReadBytes(ModernVersion.Uses550Engine ? 32u : 16u);
        Digest = _worldPacket.ReadBytes(24);

        UseIPv6 = _worldPacket.HasBit();
        uint realmJoinTicketSize = _worldPacket.ReadUInt32();
        if (realmJoinTicketSize != 0)
            RealmJoinTicket = _worldPacket.ReadString(realmJoinTicketSize);
    }

    public uint RegionID;
    public uint BattlegroupID;
    public uint RealmID;
    public byte[] LocalChallenge = new byte[16];
    public byte[] Digest = new byte[24];
    public ulong DosResponse;
    public string RealmJoinTicket = string.Empty;
    public bool UseIPv6;
}

class AuthResponse : ServerPacket
{
    public AuthResponse() : base(Opcode.SMSG_AUTH_RESPONSE) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32((uint)Result);
        _worldPacket.WriteBit(SuccessInfo != null);
        _worldPacket.WriteBit(WaitInfo != null);
        _worldPacket.FlushBits();

        if (SuccessInfo != null)
        {
            _worldPacket.WriteUInt32(SuccessInfo.VirtualRealmAddress);
            _worldPacket.WriteInt32(SuccessInfo.VirtualRealms.Count);
            _worldPacket.WriteUInt32(SuccessInfo.TimeRested);
            _worldPacket.WriteUInt8(SuccessInfo.ActiveExpansionLevel);
            _worldPacket.WriteUInt8(SuccessInfo.AccountExpansionLevel);
            _worldPacket.WriteUInt32(SuccessInfo.TimeSecondsUntilPCKick);
            _worldPacket.WriteInt32(SuccessInfo.AvailableClasses.Count);
            _worldPacket.WriteInt32(SuccessInfo.Templates.Count);
            _worldPacket.WriteUInt32(SuccessInfo.CurrencyID);
            _worldPacket.WriteInt64(SuccessInfo.Time);

            foreach (var raceClassAvailability in SuccessInfo.AvailableClasses)
            {
                _worldPacket.WriteUInt8(raceClassAvailability.RaceID);
                _worldPacket.WriteInt32(raceClassAvailability.Classes.Count);

                foreach (var classAvailability in raceClassAvailability.Classes)
                {
                    _worldPacket.WriteUInt8(classAvailability.ClassID);
                    _worldPacket.WriteUInt8(classAvailability.ActiveExpansionLevel);
                    _worldPacket.WriteUInt8(classAvailability.AccountExpansionLevel);
                    // 3.4.3+ added MinActiveExpansionLevel byte. WITHOUT this, the modern
                    // 3.4.3 client reads garbage data into every class entry, catastrophically
                    // misaligning the rest of AUTH_RESPONSE — including VirtualRealms — and
                    // thus failing to match any character's GUID realmId. (Per WPP V3_4_0_45166
                    // AuthenticationHandler.cs:39-40, gated on V3_4_3_51505+.)
                    if (ModernVersion.UsesModernEngine)
                        _worldPacket.WriteUInt8(classAvailability.MinActiveExpansionLevel);
                }
            }

            _worldPacket.WriteBit(SuccessInfo.IsExpansionTrial);
            _worldPacket.WriteBit(SuccessInfo.ForceCharacterTemplate);
            _worldPacket.WriteBit(SuccessInfo.NumPlayersHorde.HasValue);
            _worldPacket.WriteBit(SuccessInfo.NumPlayersAlliance.HasValue);
            _worldPacket.WriteBit(SuccessInfo.ExpansionTrialExpiration.HasValue);
            // A sixth bit, "has CurrentBuild", arrived with the modern engine. The proxy never
            // fills that block in, but the bit still has to be there or every field after it lands
            // one bit out.
            if (ModernVersion.Uses550Engine)
                _worldPacket.WriteBit(false);
            _worldPacket.FlushBits();

            {
                _worldPacket.WriteUInt32(SuccessInfo.GameTimeInfo.BillingPlan);
                _worldPacket.WriteUInt32(SuccessInfo.GameTimeInfo.TimeRemain);
                _worldPacket.WriteUInt32(SuccessInfo.GameTimeInfo.Unknown735);
                // 3x same bit is not a mistake - preserves legacy client behavior of BillingPlanFlags::SESSION_IGR
                _worldPacket.WriteBit(SuccessInfo.GameTimeInfo.InGameRoom); // inGameRoom check in function checking which lua event to fire when remaining time is near end - BILLING_NAG_DIALOG vs IGR_BILLING_NAG_DIALOG
                _worldPacket.WriteBit(SuccessInfo.GameTimeInfo.InGameRoom); // inGameRoom lua return from Script_GetBillingPlan
                _worldPacket.WriteBit(SuccessInfo.GameTimeInfo.InGameRoom); // not used anywhere in the client
                _worldPacket.FlushBits();
            }

            if (SuccessInfo.NumPlayersHorde.HasValue)
                _worldPacket.WriteUInt16(SuccessInfo.NumPlayersHorde.Value);

            if (SuccessInfo.NumPlayersAlliance.HasValue)
                _worldPacket.WriteUInt16(SuccessInfo.NumPlayersAlliance.Value);

            if (SuccessInfo.ExpansionTrialExpiration.HasValue)
            {
                // Widened to 64 bits on the modern engine.
                if (ModernVersion.Uses550Engine)
                    _worldPacket.WriteInt64(SuccessInfo.ExpansionTrialExpiration.Value);
                else
                    _worldPacket.WriteInt32(SuccessInfo.ExpansionTrialExpiration.Value);
            }

            foreach (VirtualRealmInfo virtualRealm in SuccessInfo.VirtualRealms)
                virtualRealm.Write(_worldPacket);

            foreach (var templat in SuccessInfo.Templates)
            {
                _worldPacket.WriteUInt32(templat.TemplateSetId);
                _worldPacket.WriteInt32(templat.Classes.Count);
                foreach (var templateClass in templat.Classes)
                {
                    _worldPacket.WriteUInt8(templateClass.ClassID);
                    _worldPacket.WriteUInt8((byte)templateClass.FactionGroup);
                }

                _worldPacket.WriteBits(templat.Name.GetByteCount(), 7);
                _worldPacket.WriteBits(templat.Description.GetByteCount(), 10);
                _worldPacket.FlushBits();

                _worldPacket.WriteString(templat.Name);
                _worldPacket.WriteString(templat.Description);
            }
        }

        if (WaitInfo != null)
            WaitInfo.Write(_worldPacket);
    }

    public AuthSuccessInfo SuccessInfo = null!; // contains the packet data in case that it has account information (It is never set when WaitInfo is set), otherwise its contents are undefined.
    public AuthWaitInfo WaitInfo = null!; // contains the queue wait information in case the account is in the login queue.
    public BattlenetRpcErrorCode Result; // the result of the authentication process, possible values are @ref BattlenetRpcErrorCode


    public enum FactionMasks : byte
    {
        Player = 1,                              // any player
        Alliance = 2,                            // player or creature from alliance team
        Horde = 4,                               // player or creature from horde team
        Monster = 8                              // aggressive creature from monster team
        // if none flags set then non-aggressive creature
    }

    public class ClassAvailability
    {
        public byte ClassID;
        public byte ActiveExpansionLevel;
        public byte AccountExpansionLevel;
        public byte MinActiveExpansionLevel;   // 3.4.3+ — added in V3_4_3_51505 per WPP

        public ClassAvailability(byte classId, byte activeExpLevel, byte accountExpLevel)
        {
            ClassID = classId;
            ActiveExpansionLevel = activeExpLevel;
            AccountExpansionLevel = accountExpLevel;
            MinActiveExpansionLevel = 0;
        }
    }

    public class RaceClassAvailability
    {
        public byte RaceID;
        public List<ClassAvailability> Classes = new();
    }

    public struct CharacterTemplateClass
    {
        public CharacterTemplateClass(FactionMasks factionGroup, byte classID)
        {
            FactionGroup = factionGroup;
            ClassID = classID;
        }

        public FactionMasks FactionGroup;
        public byte ClassID;
    }

    public class CharacterTemplate
    {
        public uint TemplateSetId;
        public List<CharacterTemplateClass> Classes = new();
        public string Name = string.Empty;
        public string Description = string.Empty;
        public byte Level;
    }

    public class AuthSuccessInfo
    {
        public byte ActiveExpansionLevel; // the current server expansion, the possible values are in @ref Expansions
        public byte AccountExpansionLevel; // the current expansion of this account, the possible values are in @ref Expansions
        public uint TimeRested; // affects the return value of the GetBillingTimeRested() client API call, it is the number of seconds you have left until the experience points and loot you receive from creatures and quests is reduced. It is only used in the Asia region in retail, it's not implemented in TC and will probably never be.

        public uint VirtualRealmAddress; // a special identifier made from the Index, BattleGroup and Region. @todo implement
        public uint TimeSecondsUntilPCKick; // @todo research
        public uint CurrencyID; // this is probably used for the ingame shop. @todo implement
        public long Time;

        public GameTime GameTimeInfo;

        public List<VirtualRealmInfo> VirtualRealms = new();     // list of realms connected to this one (inclusive) @todo implement
        public List<CharacterTemplate> Templates = new(); // list of pre-made character templates. @todo implement

        public List<RaceClassAvailability> AvailableClasses = new(); // the minimum AccountExpansion required to select the classes

        public bool IsExpansionTrial;
        public bool ForceCharacterTemplate; // forces the client to always use a character template when creating a new character. @see Templates. @todo implement
        public ushort? NumPlayersHorde; // number of horde players in this realm. @todo implement
        public ushort? NumPlayersAlliance; // number of alliance players in this realm. @todo implement
        public int? ExpansionTrialExpiration; // expansion trial expiration unix timestamp

        public struct GameTime
        {
            public uint BillingPlan;
            public uint TimeRemain;
            public uint Unknown735;
            public bool InGameRoom;
        }
    }
}

class WaitQueueUpdate : ServerPacket, ISpanWritable
{
    public WaitQueueUpdate() : base(Opcode.SMSG_WAIT_QUEUE_UPDATE) { }

    public override void Write()
    {
        WaitInfo.Write(_worldPacket);
    }

    // AuthWaitInfo: 2 uints(8) + 1 bit(1) = 9 bytes
    public int MaxSize => 9;

    public int WriteToSpan(Span<byte> buffer)
    {
        var writer = new SpanPacketWriter(buffer);
        writer.WriteUInt32(WaitInfo.WaitCount);
        writer.WriteUInt32(WaitInfo.WaitTime);
        writer.WriteBit(WaitInfo.HasFCM);
        writer.FlushBits();
        return writer.Position;
    }

    public AuthWaitInfo WaitInfo = new AuthWaitInfo();
}

class WaitQueueFinish : ServerPacket, ISpanWritable
{
    public WaitQueueFinish() : base(Opcode.SMSG_WAIT_QUEUE_FINISH) { }

    public override void Write() { }

    public int MaxSize => 0;

    public int WriteToSpan(Span<byte> buffer) => 0;
}

class ConnectTo : ServerPacket
{
    public ConnectTo() : base(Opcode.SMSG_CONNECT_TO)
    {
        Payload = new ConnectPayload();
    }

    public override void Write()
    {
        if (ModernVersion.Uses550Engine)
        {
            WriteModern();
            return;
        }

        ByteBuffer whereBuffer = new();
        whereBuffer.WriteUInt8((byte)Payload.Where.Type);

        switch (Payload.Where.Type)
        {
            case AddressType.IPv4:
                whereBuffer.WriteBytes(Payload.Where.IPv4);
                break;
            case AddressType.IPv6:
                whereBuffer.WriteBytes(Payload.Where.IPv6);
                break;
            case AddressType.NamedSocket:
                whereBuffer.WriteString(Payload.Where.NameSocket);
                break;
            default:
                break;
        }

        Sha256 hash = new();
        hash.Process(whereBuffer.GetData(), (int)whereBuffer.GetSize());
        hash.Process((uint)Payload.Where.Type);
        hash.Finish(BitConverter.GetBytes(Payload.Port));

        Payload.Signature = RsaCrypt.RSA.SignHash(hash.Digest!, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).Reverse().ToArray();

        _worldPacket.WriteBytes(Payload.Signature, (uint)Payload.Signature.Length);
        _worldPacket.WriteBytes(whereBuffer);
        _worldPacket.WriteUInt16(Payload.Port);
        _worldPacket.WriteUInt32((uint)Serial);
        _worldPacket.WriteUInt8(Con);
        _worldPacket.WriteUInt64(Key);
    }

    public ulong Key;
    /// <summary>
    /// The 5.5.0-engine layout, read straight off the client's parser (case RVA 0x617164) and
    /// identical to TrinityCore master. Three things changed from the older form: the RSA
    /// signature is gone entirely, the single address became a counted list, and each entry
    /// carries a bleep token whose three string lengths are bit-packed as 5, 24 and 6 bits.
    /// </summary>
    void WriteModern()
    {
        _worldPacket.WriteUInt32(1);                    // one address; the client accepts a list
        _worldPacket.WriteUInt32((uint)Serial);
        _worldPacket.WriteUInt8(Con);
        _worldPacket.WriteUInt64(Key);
        _worldPacket.WriteUInt32(NativeRealmAddress);
        _worldPacket.WriteUInt32(Key3);

        _worldPacket.WriteUInt8((byte)Payload.Where.Type);
        switch (Payload.Where.Type)
        {
            case AddressType.IPv4:
                _worldPacket.WriteBytes(Payload.Where.IPv4);
                break;
            case AddressType.IPv6:
                _worldPacket.WriteBytes(Payload.Where.IPv6);
                break;
            case AddressType.NamedSocket:
                _worldPacket.WriteCString(Payload.Where.NameSocket);
                break;
        }
        _worldPacket.WriteUInt16(Payload.Port);

        // BleepToken: Token, ProxyId (a C string) and Address are all empty, so the three lengths
        // and the lifespan are the whole of it. The bits flush to five bytes before the uint64.
        _worldPacket.WriteBits(0, 5);                   // Token length
        _worldPacket.WriteBits(0, 24);                  // ProxyId length
        _worldPacket.WriteBits(0, 6);                   // Address length
        _worldPacket.WriteUInt64(0);                    // TokenLifespan, nanoseconds
    }

    public ConnectToSerial Serial;

    /// <summary>Realm this connection belongs to. Zero works; the client stores it verbatim.</summary>
    public uint NativeRealmAddress;

    /// <summary>Third key component, echoed back in CMSG_AUTH_CONTINUED_SESSION.</summary>
    public uint Key3;
    public ConnectPayload Payload;
    public byte Con;

    public class ConnectPayload
    {
        public SocketAddress Where;
        public ushort Port;
        public byte[] Signature = new byte[256];
    }

    public struct SocketAddress
    {
        public AddressType Type;

        public byte[] IPv4;
        public byte[] IPv6;
        public string NameSocket;
    }

    public enum AddressType
    {
        IPv4 = 1,
        IPv6 = 2,
        NamedSocket = 3 // not supported by windows client
    }
}

class AuthContinuedSession : ClientPacket
{
    public AuthContinuedSession(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        if (ModernVersion.Uses550Engine)
        {
            // 80 bytes, matching TrinityCore master: the challenge doubled to 32 bytes, Key moved
            // behind the digest, and NativeRealmAddress and Key3 were added. Reading the old
            // 56-byte layout takes Key from the first eight bytes of the challenge, so the
            // connection type decodes to garbage and the instance connection is refused.
            DosResponse = _worldPacket.ReadUInt64();
            LocalChallenge = _worldPacket.ReadBytes(32);
            Digest = _worldPacket.ReadBytes(24);
            Key = _worldPacket.ReadUInt64();
            NativeRealmAddress = _worldPacket.ReadUInt32();
            Key3 = _worldPacket.ReadUInt32();
            return;
        }

        DosResponse = _worldPacket.ReadUInt64();
        Key = _worldPacket.ReadUInt64();
        LocalChallenge = _worldPacket.ReadBytes(16);
        Digest = _worldPacket.ReadBytes(24);
    }

    public ulong DosResponse;
    public ulong Key;
    public uint NativeRealmAddress;
    public uint Key3;
    public byte[] LocalChallenge = new byte[16];
    public byte[] Digest = new byte[24];
}

class ResumeComms : ServerPacket, ISpanWritable
{
    public ResumeComms(ConnectionType connection) : base(Opcode.SMSG_RESUME_COMMS, connection) { }

    public override void Write() { }

    public int MaxSize => 0;

    public int WriteToSpan(Span<byte> buffer) => 0;
}

class ConnectToFailed : ClientPacket
{
    public ConnectToFailed(WorldPacket packet) : base(packet) { }

    public override void Read()
    {
        // The 5.5.0 engine reversed these two.
        if (ModernVersion.Uses550Engine)
        {
            Con = _worldPacket.ReadUInt8();
            Serial = (ConnectToSerial)_worldPacket.ReadUInt32();
        }
        else
        {
            Serial = (ConnectToSerial)_worldPacket.ReadUInt32();
            Con = _worldPacket.ReadUInt8();
        }
    }

    public ConnectToSerial Serial;
    byte Con;
}

class EnterEncryptedMode : ServerPacket
{
    byte[] EncryptionKey;
    bool Enabled;

    // The HMAC input seed used up to 3.4.3: 16 bytes, hashed with SHA-256. Retail/TBC-Classic/Era
    // (<= 2.5.x) signs the HMAC output with the server's RSA private key; the client verifies with
    // the baked-in RSA public key and the client-embedded `EnableEncryptionSeed`.
    static readonly byte[] EnableEncryptionSeed = { 0x90, 0x9C, 0xD0, 0x50, 0x5A, 0x2C, 0x14, 0xDD, 0x5C, 0x2C, 0xC0, 0x64, 0x14, 0xF3, 0xFE, 0xC9 };

    // The 5.5.0-generation engine changed all three parts of the input: a different seed, twice as
    // long, hashed with SHA-512 instead of SHA-256. Values from current CypherCore, which tracks
    // retail. The signature scheme (Ed25519ctx, same context and key) did not change.
    static readonly byte[] EnableEncryptionSeed512 =
    {
        0x66, 0xBE, 0x29, 0x79, 0xEF, 0xF2, 0xD5, 0xB5, 0x61, 0x53, 0xF6, 0x5F, 0x45, 0xAE, 0x81, 0xCB,
        0x32, 0xEC, 0x94, 0xEC, 0x75, 0xB3, 0x5F, 0x44, 0x6A, 0x63, 0x43, 0x67, 0x17, 0x20, 0x44, 0x34
    };

    // WotLK Classic 3.4.3+ replaced the RSA signature with Ed25519ctx (RFC 8032) using a
    // fixed context string. Private key and context are Blizzard constants; the client has
    // the matching public key + context baked in. Values match the advocaite/HermesProxy-WOTLK
    // fork, cross-checked against the published 3.4.3 client binary's signature verification.
    static readonly byte[] Ed25519Context =
    {
        0xA7, 0x1F, 0xB6, 0x9B, 0xC9, 0x7C, 0xDD, 0x96, 0xE9, 0xBB, 0xB8, 0x21, 0x39, 0x8D, 0x5A, 0xD4
    };
    static readonly byte[] Ed25519PrivateKey =
    {
        0x08, 0xBD, 0xC7, 0xA3, 0xCC, 0xC3, 0x4F, 0x3F, 0x6A, 0x0B, 0xFF, 0xCF, 0x31, 0xC1, 0xB6, 0x97,
        0x69, 0x1E, 0x72, 0x9A, 0x0A, 0xAB, 0x2C, 0x77, 0xC3, 0x6F, 0x8A, 0xE7, 0x5A, 0x9A, 0xA7, 0xC9
    };

    /// <param name="regionGroup">
    /// Which region group's key the client should verify the signature with. Disassembly of the
    /// 2.5.6 handler shows the client looks this up in a table and, when the lookup misses, skips
    /// the signature check entirely and simply never acknowledges — no error, no disconnect. So a
    /// wrong value here is indistinguishable from a wrong signature on the wire, and the value has
    /// to be found by probing.
    /// </param>
    public EnterEncryptedMode(byte[] encryptionKey, bool enabled, int regionGroup = 0,
                              string? layout = null)
        : base(Opcode.SMSG_ENTER_ENCRYPTED_MODE)
    {
        EncryptionKey = encryptionKey;
        Enabled = enabled;
        RegionGroup = regionGroup;
        Layout = layout;
    }

    int RegionGroup;
    string? Layout;

    public override void Write()
    {
        byte[] toSign;
        if (ModernVersion.Uses550Engine)
        {
            HmacSha512 hash = new(EncryptionKey);
            hash.Process(BitConverter.GetBytes(Enabled), 1);
            hash.Finish(EnableEncryptionSeed512, 32);
            toSign = hash.Digest!;
        }
        else
        {
            HmacSha256 hash = new(EncryptionKey);
            hash.Process(BitConverter.GetBytes(Enabled), 1);
            hash.Finish(EnableEncryptionSeed, 16);
            toSign = hash.Digest!;
        }

        if (ModernVersion.Uses550Engine)
        {
            // FIXME(256-spike): the client silently ignores the current retail layout
            // (int32 RegionGroup *before* the 64-byte signature — TC master / CypherCore 11.1.7+),
            // which is what we already sent on the wire. RegionGroup travels with the SHA-512
            // scheme in every reference core, so the field is probably present but positioned
            // differently on this build. HERMES_ENC_LAYOUT selects the layout so all three
            // candidates can be tried from one build, no rebuild:
            //   "after"           = signature, then int32 RegionGroup  (CypherCore 11.1.0, the
            //                       first SHA-512 build) — highest-probability untried layout
            //   "none"            = signature only, no RegionGroup      (CypherCore 12.0.0-era)
            //   "before"(default) = int32 RegionGroup, then signature   (already disproven)
            // Body is 65 bytes for "none", 69 otherwise; "before"/"after" differ by whether the
            // leading or trailing 4 bytes of the existing hex dump are the zero RegionGroup.
            switch (Layout ?? Environment.GetEnvironmentVariable("HERMES_ENC_LAYOUT"))
            {
                case "after":
                    WriteEd25519(toSign);
                    _worldPacket.WriteInt32(RegionGroup);
                    break;
                case "none":
                    WriteEd25519(toSign);
                    break;
                default:
                    _worldPacket.WriteInt32(RegionGroup);
                    WriteEd25519(toSign);
                    break;
            }
        }
        // 3.4.3 was where the switch from RSA to Ed25519ctx happened; every build on that engine
        // or newer uses Ed25519.
        else if (ModernVersion.UsesModernEngine)
            WriteEd25519(toSign);
        else
            WriteRsa(toSign);

        _worldPacket.WriteBit(Enabled);
        _worldPacket.FlushBits();
    }

    void WriteRsa(byte[] toSign)
    {
        _worldPacket.WriteBytes(RsaCrypt.RSA.SignHash(toSign, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).Reverse().ToArray());
    }

    void WriteEd25519(byte[] toSign)
    {
        var privateKey = new Org.BouncyCastle.Crypto.Parameters.Ed25519PrivateKeyParameters(Ed25519PrivateKey, 0);
        var signer = new Org.BouncyCastle.Crypto.Signers.Ed25519ctxSigner(Ed25519Context);
        signer.Init(forSigning: true, privateKey);
        signer.BlockUpdate(toSign, 0, toSign.Length);
        _worldPacket.WriteBytes(signer.GenerateSignature());
    }
}

//Structs
public class AuthWaitInfo
{
    public void Write(WorldPacket data)
    {
        data.WriteUInt32(WaitCount);
        data.WriteUInt32(WaitTime);
        // The modern engine added a faction-group restriction byte and a second bit here. The
        // proxy has no value for either, but their absence would shift everything after them.
        if (ModernVersion.Uses550Engine)
            data.WriteUInt8(0);
        data.WriteBit(HasFCM);
        if (ModernVersion.Uses550Engine)
            data.WriteBit(false);
        data.FlushBits();
    }

    public uint WaitCount; // position of the account in the login queue
    public uint WaitTime; // Wait time in login queue in minutes, if sent queued and this value is 0 client displays "unknown time"
    public bool HasFCM; // true if the account has a forced character migration pending. @todo implement
}

struct VirtualRealmNameInfo
{
    public VirtualRealmNameInfo(bool isHomeRealm, bool isInternalRealm, string realmNameActual, string realmNameNormalized)
    {
        IsLocal = isHomeRealm;
        IsInternalRealm = isInternalRealm;
        RealmNameActual = realmNameActual;
        RealmNameNormalized = realmNameNormalized;
    }

    public void Write(WorldPacket data)
    {
        data.WriteBit(IsLocal);
        data.WriteBit(IsInternalRealm);
        data.WriteBits(RealmNameActual.GetByteCount(), 8);
        data.WriteBits(RealmNameNormalized.GetByteCount(), 8);
        data.FlushBits();

        data.WriteString(RealmNameActual);
        data.WriteString(RealmNameNormalized);
    }

    public bool IsLocal;                    // true if the realm is the same as the account's home realm
    public bool IsInternalRealm;            // @todo research
    public string RealmNameActual;     // the name of the realm
    public string RealmNameNormalized; // the name of the realm without spaces
}

struct VirtualRealmInfo
{
    public VirtualRealmInfo(uint realmAddress, bool isHomeRealm, bool isInternalRealm, string realmNameActual, string realmNameNormalized)
    {

        RealmAddress = realmAddress;
        RealmNameInfo = new VirtualRealmNameInfo(isHomeRealm, isInternalRealm, realmNameActual, realmNameNormalized);
    }

    public void Write(WorldPacket data)
    {
        data.WriteUInt32(RealmAddress);
        RealmNameInfo.Write(data);
    }

    public uint RealmAddress;             // the virtual address of this realm, constructed as RealmHandle::Region << 24 | RealmHandle::Battlegroup << 16 | RealmHandle::Index
    public VirtualRealmNameInfo RealmNameInfo;
}
