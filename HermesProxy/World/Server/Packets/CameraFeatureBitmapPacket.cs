using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

/// <summary>
/// The message that builds the client's 256-bit feature-enable bitmap — the table whose absence has
/// crashed every movement attempt in this project.
///
/// Traced end to end in the client image: the group-0x46 dispatcher at RVA 0x61C350 dispatches on
/// `opcode - 0x460000`, and slot 0x272 lands on the case at 0x626401. That case builds a message
/// object, copies the remaining packet bytes into an inline buffer, stores the blob at object+0x20,
/// and calls the thunk at 0x5C8CC0, which tail-jumps to the builder 0x1A73C70. The builder allocates
/// the table, stores its pointer at RVA 0x42C03D0, and `movups`-copies 32 bytes out of the blob.
///
/// After that `IsEnabled(index)` at 0x1A73B80 works. The proxy has never sent this message, so the
/// pointer stays null and the first read of it — the camera, on any movement-flag change — faults at
/// 0x1A73BA3. That fault accounts for ten of the crash reports across two days.
///
/// The body is a raw blob; only its first 32 bytes are consumed. The name in our opcode table,
/// SMSG_TRIGGER_CINEMATIC, is one of the unverified CypherCore-aligned guesses and contradicts the
/// blob shape, so treat 0x460272 as a number rather than as that message.
///
/// All-zero means every feature reports disabled, which is what the client already behaves as while
/// the table is null — the difference is that a zeroed table can be *read*. If zeros turn out to
/// disable something needed, the real 32 bytes can be captured by breakpointing 0x1A73C70 in a
/// client that does not crash.
///
/// HERMES_256_CAMERATABLE=1 enables it. Confirm with tools-256-spike/readglobal.py that 0x42C03D0
/// goes non-null after world entry, then try walking.
/// </summary>
public class CameraFeatureBitmap : ServerPacket
{
    public static readonly bool Enabled =
        System.Environment.GetEnvironmentVariable("HERMES_256_CAMERATABLE") == "1";

    public CameraFeatureBitmap() : base(Opcode.SMSG_TRIGGER_CINEMATIC) { }

    public override void Write()
    {
        // The builder copies 32 bytes; send exactly that so a wrong length cannot be the variable
        // under test. It reads the blob twice, 16 bytes at a time.
        for (int i = 0; i < 32; ++i)
            _worldPacket.WriteUInt8(0);
    }
}
