using HermesProxy.World.Enums;

namespace HermesProxy.World.Objects.Version.V2_5_6_69110;

/// <summary>
/// HERMES_256_NPCGOSSIPBIT=1 announces <see cref="NPCFlags.Gossip"/> on any NPC that offers a
/// service, so this build's client will initiate an interaction with it at all.
/// </summary>
/// <remarks>
/// <para>
/// MEASURED LIVE 29 Aug, in Anvilmar, and the sample is unusually clean because the NPCs stand
/// together and their create blocks differ in exactly one field. Every trainer there — Thran
/// Khorman, Solm Hargrin, Thorgas Grimson, Branstock Khalder, Marryk Nurribit, Bromos Grummner,
/// Alamar Grimm — carries cmangos <c>NpcFlags 19</c> = <c>Gossip|QuestGiver|Trainer</c> and works:
/// the client sends <c>CMSG_SET_SELECTION</c>, then <c>CMSG_TALK_TO_GOSSIP</c>, and the frame
/// opens. Adlin Pridedrift (<c>130 = QuestGiver|Vendor</c>), Wren Darkspring (130), Rybrad
/// Coldbank and Grundel Harkin (<c>4224 = Vendor|Repair</c>) cannot be interacted with at all: the
/// client selects the unit and then sends <b>nothing</b>.
/// </para>
/// <para>
/// The field is not misread, and this is not an alignment fault. Their unit-create lines are
/// identical to the trainer's apart from <c>NpcFlags</c> itself — same Health 100/100, same Level,
/// same faction 55, same null UnitFlags — and the value that reaches the wire is the right one
/// (<c>npcFlags=130</c> is in the log for Adlin, <c>19</c> for the trainer). The client simply does
/// not initiate without bit 0. Note the exclusion this also buys: Adlin carries QuestGiver too, and
/// the client does not act on that either, so it is bit 0 specifically and not "any service bit".
/// </para>
/// <para>
/// That is this engine's design rather than a fault. On 5.5.x every NPC service is reached
/// <i>through</i> gossip, and live agrees: in capture #13 the vendor inventory arrives after
/// <c>CMSG_GOSSIP_SELECT_OPTION</c> and never after a bare <c>CMSG_LIST_INVENTORY</c>
/// (REFERENCE-256-CLIENT.md §137.1). cmangos 2.4.3 does not set Gossip on a plain vendor because
/// the 2.4.3 client opened the vendor frame directly — a path this client no longer has. So the
/// mismatch is an era difference in what the flag *means*, not a wrong value.
/// </para>
/// <para>
/// Once the bit is announced, nothing else has to be synthesised: cmangos answers
/// <c>CMSG_GOSSIP_HELLO</c> from its own <c>PrepareGossipMenu</c>, which builds the vendor,
/// trainer and banker options out of the same npcflags, and the existing gossip path in
/// <c>NPCHandler</c> carries the rest. That path is already proven — it is what the trainer used.
/// </para>
/// <para>
/// The trigger set is deliberately the services cmangos can serve through a gossip menu, not
/// "any non-zero flag". <see cref="NPCFlags.SpellClick"/> and <see cref="NPCFlags.PlayerVehicle"/>
/// are click-driven and have no menu behind them, so announcing a gossip they cannot answer would
/// be inventing behaviour rather than restoring it — the failure mode this project has paid for
/// twice. <c>Unk1</c>/<c>Unk2</c> are excluded for the same reason: no known meaning, no reason to
/// act on them.
/// </para>
/// </remarks>
internal static class NpcGossipBit256
{
    public static readonly bool Enabled =
        System.Environment.GetEnvironmentVariable("HERMES_256_NPCGOSSIPBIT") == "1";

    /// <summary>
    /// Services cmangos 2.4.3 offers through a gossip menu. Everything here is something
    /// <c>PrepareGossipMenu</c> can turn into an option; nothing here is click-driven.
    /// </summary>
    private const uint ServiceMask =
        (uint)(NPCFlags.QuestGiver
             | NPCFlags.Trainer | NPCFlags.TrainerClass | NPCFlags.TrainerProfession
             | NPCFlags.Vendor | NPCFlags.VendorAmmo | NPCFlags.VendorFood
             | NPCFlags.VendorPoison | NPCFlags.VendorReagent
             | NPCFlags.Repair | NPCFlags.FlightMaster
             | NPCFlags.SpiritHealer | NPCFlags.SpiritGuide
             | NPCFlags.Innkeeper | NPCFlags.Banker | NPCFlags.Petitioner
             | NPCFlags.TabardDesigner | NPCFlags.BattleMaster | NPCFlags.Auctioneer
             | NPCFlags.StableMaster | NPCFlags.GuildBanker);

    /// <summary>
    /// Returns <paramref name="npcFlags"/> with <see cref="NPCFlags.Gossip"/> set when the knob is
    /// on and the unit offers a service. Length-neutral — this is a value change inside a u32 that
    /// is written either way, so it cannot shift the create block.
    /// </summary>
    public static uint Apply(uint npcFlags)
    {
        if (!Enabled || (npcFlags & ServiceMask) == 0)
            return npcFlags;

        return npcFlags | (uint)NPCFlags.Gossip;
    }
}
