using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HermesProxy.World;

public sealed class SniffFile
{
    // Monotonic counter suffixed to filenames so simultaneous sessions can't collide on the
    // one-second-granular Unix timestamp (two logins in the same second previously raced for
    // the same path).
    private static int _sessionCounter = 0;

    // 64 KB FileStream buffer — packet logging is bursty sequential writes; the default 4 KB
    // buffer means more frequent syscalls. Larger buffer reduces kernel transitions.
    private const int FileBufferSize = 64 * 1024;

    public SniffFile(string fileName, ushort build)
    {
        string dir = "PacketsLog";
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        int seq = Interlocked.Increment(ref _sessionCounter);
        string file = fileName + "_" + build + "_" + Time.UnixTime + "_" + seq + ".pkt";
        string path = Path.Combine(dir, file);

        var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            FileBufferSize,
            FileOptions.SequentialScan);
        _fileWriter = new BinaryWriter(stream);
        _gameVersion = build;
    }
    BinaryWriter _fileWriter;
    ushort _gameVersion;
    readonly Lock _lock = new();
    bool _closed;

    public void WriteHeader()
    {
        try { WriteHeaderUnguarded(); } catch { _writeFailed = true; }
    }

    void WriteHeaderUnguarded()
    {
        _fileWriter.Write('P');
        _fileWriter.Write('K');
        _fileWriter.Write('T');
        UInt16 sniffVersion = 0x201;
        _fileWriter.Write(sniffVersion);
        _fileWriter.Write(_gameVersion);

        for (int i = 0; i < 40; i++)
        {
            byte zero = 0;
            _fileWriter.Write(zero);
        }
    }

    /// <summary>
    /// DIAGNOSTIC LOGGING MUST NEVER BREAK THE PROXY, and until 23 Aug it could.
    ///
    /// WorldSocket.SendPacket calls Packet.LogPacket BEFORE the send and does not guard it, and
    /// nothing in here caught anything - so a BinaryWriter failure propagated out of the logger,
    /// past the send, and the packet was never put on the wire. The client then sees a stream with
    /// a whole SMSG missing, which is indistinguishable from a protocol bug and is intermittent by
    /// construction.
    ///
    /// MEASURED, 23 Aug: the machine's C: drive reached 100% (56 MB free) and eight of the day's
    /// captures are EXACT multiples of the 64 KB FileStream buffer - 1x, 1x, 3x, 6x, 9x, 10x, 16x -
    /// which is the signature of a buffer flush that succeeded N times and then threw. The two
    /// captures from the 12:44 freeze session are both exactly 1 x 65536. Clearing the client's WDB
    /// cache freed disk space and was followed by ten clean world entries; the failures resumed as
    /// it filled again, which fits the disk far better than it fits the cache.
    ///
    /// So: swallow everything, and latch off after the first failure rather than throwing once per
    /// packet for the rest of the session. Losing the sniff is an acceptable cost; losing a packet
    /// is not.
    /// </summary>
    bool _writeFailed;

    public void WritePacket(uint opcode, bool isFromClient, ReadOnlySpan<byte> data)
    {
        lock (_lock)
        {
            if (_closed || _writeFailed)
                return;

            try
            {
                WritePacketUnguarded(opcode, isFromClient, data);
            }
            catch (Exception e)
            {
                _writeFailed = true;
                try
                {
                    Framework.Logging.Log.Print(Framework.Logging.LogType.Error,
                        $"Packet log write failed ({e.GetType().Name}: {e.Message}) - packet logging " +
                        "is now OFF for this session. Check free disk space. Sends are unaffected.");
                }
                catch { }
            }
        }
    }

    void WritePacketUnguarded(uint opcode, bool isFromClient, ReadOnlySpan<byte> data)
    {
        {
            byte direction = !isFromClient ? (byte)0xff : (byte)0x0;
            _fileWriter.Write(direction);

            uint unixtime = (uint)Time.UnixTime;
            _fileWriter.Write(unixtime);
            _fileWriter.Write(Environment.TickCount);

            if (isFromClient)
            {
                uint packetSize = (uint)(data.Length - 2 + sizeof(uint));
                _fileWriter.Write(packetSize);
                _fileWriter.Write(opcode);

                // Skip the 2-byte opcode prefix; single bulk write of the payload.
                _fileWriter.Write(data[2..]);
            }
            else
            {
                uint packetSize = (uint)data.Length + sizeof(ushort);
                _fileWriter.Write(packetSize);
                ushort opcode2 = (ushort)opcode;
                _fileWriter.Write(opcode2);
                _fileWriter.Write(data);
            }
        }
    }

    public void CloseFile()
    {
        // Idempotent: callers (CMSG_LOG_DISCONNECT in WorldSocket.ReadData, GlobalSessionData.OnDisconnect)
        // race on the unsynchronised ModernSniff field; first wins, repeats must no-op rather than
        // throw ObjectDisposedException on the already-closed FileStream (issue #75).
        lock (_lock)
        {
            if (_closed)
                return;
            _closed = true;
            try { _fileWriter.Flush(); } catch { }
            try { _fileWriter.Close(); } catch { }
        }
    }
}
