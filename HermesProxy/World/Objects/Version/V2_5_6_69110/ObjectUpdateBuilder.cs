// The 2.5.6 Anniversary client runs the WowCS entity-fragment object model that arrived with the
// 11.x engine, not the flat descriptor layout every other build in this repository uses. Proof from
// the client binary: it carries EntityFragmentDebugInfo (fragmentID, archetypeID, isAuth,
// storageType, fragmentName, mirrorType), the WowCS namespace, and the full fragment class table at
// RVA 0x3492538 - CGObject_C, CGUnit_C, CGPlayer_C, CGActivePlayer_C, CGItem_C and the rest - plus
// the tag names Tag_Player, Tag_Unit, Tag_ActivePlayer, CActor, FMeshObjectData_C, matching
// TrinityCore master's WowCS::EntityFragment enum name for name.
//
// Only the framing differs. The values themselves are still bitmask blocks written exactly as the
// 2.5.3 builder writes them, which is why this file starts as a copy of it. What changed:
//
//   create:  u8 type, guid, u8 objectType, <movement>,
//            u32 size, u8 fieldFlags, <fragment ids> 0xFF, u8 1 (CGObject is indirect), <values>
//   values:  u8 type, guid, u32 size, u8 hasOwner, u8 idsChanged=0, u8 contentsChangedMask, <values>
//
// The int32 HeirFlags that follows objectType on older builds is gone. Field indices are still
// 2.5.3's and are the next thing to verify.

using Framework.GameMath;
using Framework.IO;
using HermesProxy.World.Enums.V2_5_3_41750;
using HermesProxy.World.Server.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HermesProxy.World.Objects.Version.V2_5_6_69110;

public partial class ObjectUpdateBuilder
{
    static readonly bool s_sentinel =
        Environment.GetEnvironmentVariable("HERMES_256_SENTINEL") == "1";

    static readonly int s_createPad =
        int.TryParse(Environment.GetEnvironmentVariable("HERMES_256_PAD"), out var p) ? p : 0;

    public ObjectUpdateBuilder(ObjectUpdate updateData, GameSessionData gameState)
    {
        m_alreadyWritten = false;
        m_updateData = updateData;
        m_gameState = gameState;

        Enums.ObjectType objectType = updateData.Guid.GetObjectType();
        // HERMES_256_CONTAINERONLY, second half. On a VALUES update CreateData is null, so the type
        // came from the guid alone - and a bag's modern guid is an ITEM guid (Blizzard's own bag in
        // live4_s3_deb reads `Object Guid: ... Item/0`). So every container values update was built
        // as an Item, the `case Container` arm never ran, BuildContainerDataUpdate was never called,
        // and the bag's own Slots never went out. Measured: cmangos sends the block, we resolve it
        // to Container in the handler and fill Slots (CONTAINERMASK showed the exact set bits -
        // SLOT_1=62, and a move to bag slot 0 set 62,63), and the builder then dropped it on the
        // floor. That is why an item moved out of a bag left its source slot greyed for ever.
        // GetOriginalObjectType falls back to the guid's own type, so this is safe for objects we
        // never saw created.
        if (s_containerOnly && updateData.CreateData == null)
            objectType = gameState.GetOriginalObjectType(updateData.Guid);
        if (updateData.CreateData != null)
        {
            objectType = updateData.CreateData.ObjectType;
            if (updateData.CreateData.ThisIsYou)
                objectType = Enums.ObjectType.ActivePlayer;
        }
        if (objectType == Enums.ObjectType.Player && m_gameState.CurrentPlayerGuid == updateData.Guid)
            objectType = Enums.ObjectType.ActivePlayer;
        m_objectType = ObjectTypeConverter.ConvertToBCC(objectType);
        m_objectTypeMask = Enums.ObjectTypeMask.Object;

        uint fieldsSize;
        uint dynamicFieldsSize;
        switch (m_objectType)
        {
            case Enums.ObjectTypeBCC.Item:
                fieldsSize = (uint)ItemField.ITEM_END;
                dynamicFieldsSize = (uint)ItemDynamicField.ITEM_DYNAMIC_END;
                m_objectTypeMask |= Enums.ObjectTypeMask.Item;
                break;
            case Enums.ObjectTypeBCC.Container:
                fieldsSize = (uint)ContainerField.CONTAINER_END;
                dynamicFieldsSize = (uint)ContainerDynamicField.CONTAINER_DYNAMIC_END;
                m_objectTypeMask |= Enums.ObjectTypeMask.Item;
                m_objectTypeMask |= Enums.ObjectTypeMask.Container;
                break;
            case Enums.ObjectTypeBCC.Unit:
                fieldsSize = (uint)UnitField.UNIT_END;
                dynamicFieldsSize = (uint)UnitDynamicField.UNIT_DYNAMIC_END;
                m_objectTypeMask |= Enums.ObjectTypeMask.Unit;
                break;
            case Enums.ObjectTypeBCC.Player:
                fieldsSize = (uint)PlayerField.PLAYER_END;
                dynamicFieldsSize = (uint)PlayerDynamicField.PLAYER_DYNAMIC_END;
                m_objectTypeMask |= Enums.ObjectTypeMask.Unit;
                m_objectTypeMask |= Enums.ObjectTypeMask.Player;
                break;
            case Enums.ObjectTypeBCC.ActivePlayer:
                fieldsSize = (uint)ActivePlayerField.ACTIVE_PLAYER_END;
                dynamicFieldsSize = (uint)ActivePlayerDynamicField.ACTIVE_PLAYER_DYNAMIC_END;
                m_objectTypeMask |= Enums.ObjectTypeMask.Unit;
                m_objectTypeMask |= Enums.ObjectTypeMask.Player;
                m_objectTypeMask |= Enums.ObjectTypeMask.ActivePlayer;
                break;
            case Enums.ObjectTypeBCC.GameObject:
                fieldsSize = (uint)GameObjectField.GAMEOBJECT_END;
                dynamicFieldsSize = (uint)GameObjectDynamicField.GAMEOBJECT_DYNAMIC_END;
                m_objectTypeMask |= Enums.ObjectTypeMask.GameObject;
                break;
            case Enums.ObjectTypeBCC.DynamicObject:
                fieldsSize = (uint)DynamicObjectField.DYNAMICOBJECT_END;
                dynamicFieldsSize = (uint)DynamicObjectDynamicField.DYNAMICOBJECT_DYNAMIC_END;
                m_objectTypeMask |= Enums.ObjectTypeMask.DynamicObject;
                break;
            case Enums.ObjectTypeBCC.Corpse:
                fieldsSize = (uint)CorpseField.CORPSE_END;
                dynamicFieldsSize = (uint)CorpseDynamicField.CORPSE_DYNAMIC_END;
                m_objectTypeMask |= Enums.ObjectTypeMask.Corpse;
                break;
            default:
                throw new ArgumentOutOfRangeException("Unsupported object type!");
        }

        m_dynamicFields = new(dynamicFieldsSize, m_updateData.Type);

        lock (m_gameState.ObjectCacheLock)
        {
            if (m_updateData.CreateData == null &&
                m_gameState.ObjectCacheModern.TryGetValue(updateData.Guid, out m_fields!) &&
                m_fields != null)
            {
                m_fields.m_updateMask.Clear();
            }
            else
            {
                m_fields = new UpdateFieldsArray(fieldsSize);
                m_gameState.ObjectCacheModern.Remove(updateData.Guid);
                m_gameState.ObjectCacheModern.Add(updateData.Guid, m_fields);
            }
        }
    }

    protected bool m_alreadyWritten;
    protected ObjectUpdate m_updateData;
    protected UpdateFieldsArray m_fields;
    protected DynamicUpdateFieldsArray m_dynamicFields;
    protected Enums.ObjectTypeBCC m_objectType;
    protected Enums.ObjectTypeMask m_objectTypeMask;
    protected CreateObjectBits m_createBits;
    protected GameSessionData m_gameState;

    /// <summary>WowCS::EntityFragment, from TrinityCore master. Only the ones we ever emit.</summary>
    enum EntityFragment : byte
    {
        FEntityPosition   = 1,
        FMirroredObject   = 18,   // kind 0, 12 bytes, a real deserialiser at RVA 0x245760
        Tag_ActiveObject  = 217,
        Tag_VisibleObject = 218,
        CGObject         = 2,     // updateable and indirect
        Tag_Item         = 200,
        Tag_Container    = 201,
        Tag_Unit         = 204,
        Tag_Player       = 205,
        Tag_GameObject   = 206,
        Tag_DynamicObject = 207,
        Tag_Corpse       = 208,
        Tag_ActivePlayer = 215,
        End              = 255,
    }

    /// <summary>UF::UpdateFieldFlag.</summary>
    [System.Flags]
    enum FieldVisibility : byte
    {
        None = 0, Owner = 0x01, PartyMember = 0x02, UnitAll = 0x04, Empath = 0x08,
    }

    /// <summary>
    /// Adds Tag_ActivePlayer to the active player's fragment list. Off by default: the combination
    /// once made the client fault resolving a WowCS::Archetype. Without it the client never reads
    /// the ActivePlayer descriptor at all, which is why money and XP read back as zero.
    /// </summary>
    /// <summary>
    /// Skips ActivePlayerData entirely. A diagnostic: if the client behaves exactly the same with
    /// 5632 fewer bytes in the values blob, it was never reading them.
    /// </summary>
    static readonly bool s_noApd =
        System.Environment.GetEnvironmentVariable("HERMES_256_NOAPD") == "1";

    static readonly bool s_activeTag =
        System.Environment.GetEnvironmentVariable("HERMES_256_ACTIVETAG") == "1";

    /// <summary>
    /// Replaces the malformed legacy masked values body (updateType 0) with a well-formed empty
    /// modern update: changedFragmentMask = 0, no field body. Default off = current behaviour.
    /// See the long note in WriteToPacket's non-create header for the reader evidence and the
    /// measurement that flips the default.
    /// </summary>
    static readonly bool s_valuesNoop =
        System.Environment.GetEnvironmentVariable("HERMES_256_VALUESNOOP") == "1";

    /// <summary>
    /// Route B (section 125): when HERMES_256_VALUESASCREATE re-emits eligible units as creates
    /// in the handler, the value updates that remain (players, game objects, uncreated units)
    /// must not carry the malformed legacy body either. Mirror the handler knob here so those
    /// leftovers become the same clean empty update as VALUESNOOP.
    /// </summary>
    static readonly bool s_valuesAsCreate =
        System.Environment.GetEnvironmentVariable("HERMES_256_VALUESASCREATE") == "1";

    // The legacy masked body is suppressed (replaced by a clean empty update) under either knob.
    static bool SuppressLegacyValuesBody => s_valuesNoop || s_valuesAsCreate;

    /// <summary>
    /// The fragment ids this object declares, ascending — the client's holder keeps them sorted and
    /// the wire order follows. Every object carries CGObject; the rest is one tag per type.
    /// </summary>
    List<EntityFragment> GetFragments()
    {
        // The ids below are confirmed against the client's own component registry at RVA
        // 0x3C5ED70 (0xB0-byte entries, indexed by fragment id): the names and numbers match
        // TrinityCore's WowCS::EntityFragment enum exactly, and the kind byte at +0x1C is 1 for
        // precisely the four TrinityCore calls indirect and 4 for every Tag_*.
        // CGObject, confirmed against two independent 11.x server implementations: TrinityCore
        // master and CypherCore 11.2.5 both do exactly `EntityFragments.Add(EntityFragment.CGObject,
        // false, this)` in the WorldObject constructor. The client's own heap also contains
        // [18, 217, 218] sets built on FMirroredObject_C, but those are its local objects, not
        // networked ones — sending that shape instead changed nothing.
        // Plus FMirroredObject_C. The client's error string is "jam mirror full update failure",
        // its types are named JamCliMeshObjectData / JamMirrorResearch_C, and its own networked
        // objects in the heap all carry fragment 18 — while CGObject's deserialiser is a bare
        // `ret`. The value stream on this build travels through the mirror component.
        // CGObject, confirmed against two independent 11.x implementations: TrinityCore master and
        // CypherCore 11.2.5 both do exactly `EntityFragments.Add(EntityFragment.CGObject, false,
        // this)` in the WorldObject constructor.
        var ids = new List<EntityFragment> { EntityFragment.CGObject };
        switch (m_objectType)
        {
            case Enums.ObjectTypeBCC.Item:          ids.Add(EntityFragment.Tag_Item); break;
            case Enums.ObjectTypeBCC.Container:     ids.Add(EntityFragment.Tag_Item);
                                                    ids.Add(EntityFragment.Tag_Container); break;
            case Enums.ObjectTypeBCC.Unit:          ids.Add(EntityFragment.Tag_Unit); break;
            case Enums.ObjectTypeBCC.Player:        ids.Add(EntityFragment.Tag_Unit);
                                                    ids.Add(EntityFragment.Tag_Player); break;
            // Tag_ActivePlayer was left out because the client resolved a null WowCS::Archetype
            // from the three-id combination and faulted in the function that logs "Start waiting
            // for active player". That was measured while PlayerData itself was still wrong.
            //
            // Leaving it out has a cost that is now measured: with only Tag_Unit and Tag_Player the
            // client has nothing telling it an ActivePlayer descriptor follows, so it stops after
            // PlayerData and skips the remaining 5632 bytes. Three sentinels written into Coinage,
            // XP and NextLevelXP all read back as 0 in game while UnitData's MaxPower read
            // correctly - the block is not misaligned, it is never read.
            //
            // HERMES_256_ACTIVETAG=1 adds the tag so the combination can be retried.
            case Enums.ObjectTypeBCC.ActivePlayer:  ids.Add(EntityFragment.Tag_Unit);
                                                    ids.Add(EntityFragment.Tag_Player);
                                                    if (s_activeTag)
                                                        ids.Add(EntityFragment.Tag_ActivePlayer);
                                                    break;
            case Enums.ObjectTypeBCC.GameObject:    ids.Add(EntityFragment.Tag_GameObject); break;
            case Enums.ObjectTypeBCC.DynamicObject: ids.Add(EntityFragment.Tag_DynamicObject); break;
            case Enums.ObjectTypeBCC.Corpse:        ids.Add(EntityFragment.Tag_Corpse); break;
        }
        ids.Sort();
        return ids;
    }

    /// <summary>
    /// The visibility byte at the head of the values blob. The client gates its own reads on it, so
    /// this is the single place that decides what it will even look for.
    ///
    /// PartyMember gates QuestLog[25], so emitting Owner alone means we never send a quest log and
    /// the client cannot show what it was never given. That reasoning is sound and the flag is still
    /// wrong to set: measured, the block costs 25 x 66 = 1650 bytes and takes PlayerData from 1012
    /// to 2666, where the client's own reader (0x738FB0) consumes about 1030. ActivePlayerData then
    /// starts 1650 bytes inside our PlayerData and the client hangs on the loading screen - observed
    /// 22 Aug 21:55, against a session 57 minutes earlier that differed only in this.
    ///
    /// So it is opt-in, not opt-out: HERMES_256_QUESTLOG=1 to send it. Turning it on for good needs
    /// the quest log's real home on this build first. Either the entry is smaller than the 66 bytes
    /// derived from element reader 0x742110, or - more likely, given that 1030 has no room for any
    /// 25-slot array - the log is not in PlayerData at all and belongs in ActivePlayerData, whose
    /// 6378 bytes already match its reader.
    /// </summary>
    /// <para>
    /// Items and containers report None, so the client skips the owner-gated groups - StackCount,
    /// Expiration, SpellCharges, Durability, MaxDurability, ArtifactXP, ZoneFlags - and the writer
    /// now gates exactly where the reader gates, so the block stays byte-exact either way. Every
    /// item block we send belongs to the session's own player, which is precisely the condition the
    /// flag describes, so Owner is very likely correct here; HERMES_256_ITEMOWNER=1 turns it on.
    /// It stays opt-in until one session confirms it, because it changes a block length and that is
    /// how both of 22 Aug's failures were introduced.
    /// </para>
    FieldVisibility GetFieldVisibility() =>
        m_objectType == Enums.ObjectTypeBCC.ActivePlayer
            ? (s_vejB ? FieldVisibility.Owner | FieldVisibility.PartyMember | FieldVisibility.UnitAll
               : s_questLog ? FieldVisibility.Owner | FieldVisibility.PartyMember : FieldVisibility.Owner)
            : s_itemOwner && (m_objectType == Enums.ObjectTypeBCC.Item ||
                              m_objectType == Enums.ObjectTypeBCC.Container)
                ? FieldVisibility.Owner
                : FieldVisibility.None;

    /// <summary>
    /// Bisection harness. HERMES_256_ZERO takes a comma-separated list of byte ranges to blank in
    /// the create block after it is written - `unit:398-403,player:29,ap:329-4707,ap:5091-5094`,
    /// where the offset is relative to that fragment's start and a range without a dash is one byte.
    ///
    /// It exists because the last known-good build could not be reconstructed: both descriptor
    /// files were untracked, and a field-wiring pass had shipped a large number of changes at once.
    /// Diffing a captured create against a capture from the working session gives the exact bytes
    /// that changed, and this blanks them back without touching the writers - so the block length,
    /// which is load-bearing in a linear unmasked format, cannot move while bisecting. Turn ranges
    /// off one at a time to find which value the client cannot survive.
    ///
    /// Diagnostic scaffolding. It comes out with the fault it is used to find.
    /// </summary>
    static void ApplyZeroRanges(WorldPacket body, uint oUnit, uint oPlayer, uint oActive,
                                string? soleFragment = null)
    {
        if (s_zeroRanges.Length == 0)
            return;

        foreach (var (fragment, start, length) in s_zeroRanges)
        {
            if (soleFragment != null)
            {
                // A world unit has only its own UnitData, so accept just that name here. Keeping
                // the names distinct stops a range meant for the player's UnitData from also
                // firing on every creature, which would confound the bisection.
                if (fragment == soleFragment)
                    body.ZeroRange(oUnit + start, length);
                continue;
            }

            uint fragmentBase = fragment switch
            {
                "object" => 0,
                "unit" => oUnit,
                "player" => oPlayer,
                "ap" or "activeplayer" => oActive,
                _ => uint.MaxValue,
            };
            if (fragmentBase != uint.MaxValue)
                body.ZeroRange(fragmentBase + start, length);
        }
    }

    static readonly (string Fragment, uint Start, uint Length)[] s_zeroRanges = ParseZeroRanges(
        System.Environment.GetEnvironmentVariable("HERMES_256_ZERO"));

    static (string, uint, uint)[] ParseZeroRanges(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return [];

        var parsed = new System.Collections.Generic.List<(string, uint, uint)>();
        foreach (var entry in spec.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Trim().Split(':');
            if (parts.Length != 2)
                continue;
            var bounds = parts[1].Split('-');
            if (!uint.TryParse(bounds[0], out uint from))
                continue;
            uint to = bounds.Length > 1 && uint.TryParse(bounds[1], out uint parsedTo) ? parsedTo : from;
            if (to < from)
                continue;
            parsed.Add((parts[0].Trim().ToLowerInvariant(), from, to - from + 1));
        }

        if (parsed.Count > 0)
            Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
                $"[256-spike] HERMES_256_ZERO: blanking {parsed.Count} range(s) in the create block");
        return parsed.ToArray();
    }

    static readonly bool s_itemOwner =
        System.Environment.GetEnvironmentVariable("HERMES_256_ITEMOWNER") == "1";

    static readonly bool s_questLog =
        System.Environment.GetEnvironmentVariable("HERMES_256_QUESTLOG") == "1";

    /// <summary>
    /// Vej B: emit the active-player create at the FULL visibility 0x07 (Owner|PartyMember|UnitAll)
    /// that live Blizzard sends, instead of 0x01/0x03. Confirmed against the live 0x07 create
    /// (tools-256-spike/ap_rowine.bin, Rowine): the client needs the 0x07 byte to treat the object
    /// as its own full active player (quest log / skill / bag UI), and the PlayerData/ActivePlayerData
    /// layout the proxy already emits is byte-exact to live's at 0x07 - QuestLog[25] 66B, the
    /// QuestLogExtraMap {u32 key,u32 value}, VisibleItems[19] 23B at the block end, DungeonScore 12B,
    /// MSB-first name bits, and InvSlots[146] at the ActivePlayerData head with Coinage at +300 for
    /// the empty case. There is no UnitAll-only field group (every UnitAll gate also includes Owner),
    /// so 0x07 emits the same field set as 0x03 plus the byte; the reader gating matches the writer.
    /// Default off. Pair with HERMES_256_QUESTLOG/INVSLOTS to fill the arrays, and APDPAD stays on as
    /// the tail-over-read cover.
    /// </summary>
    static readonly bool s_vejB =
        System.Environment.GetEnvironmentVariable("HERMES_256_VEJB") == "1";

    /// <summary>
    /// Live-exact create-object bits (24 Aug, measured over live5_s1_inflated.bin): the live
    /// client's bit order KEEPS AreaTrigger at bit 13 - the transport gameobject sets
    /// GameObject at bit 14 (81 4A 00) and the player sets ActivePlayer at bit 17 (8C 00 40),
    /// both one position later than this writer emitted - and HasEntityPosition is NOT
    /// hardcoded: all 9 live item creates carry 00 00 00 (no position), while all 90 unit and
    /// 46 gameobject creates carry it set. Our hardcoded first bit told the client every ITEM
    /// has a position, which broke exactly and only the item objects (paper doll, backpack),
    /// while units/gameobjects emit live-identical bytes with or without the fix because
    /// nothing they set sits above bit 12. Default off = old behavior.
    /// </summary>
    static readonly bool s_createBits =
        System.Environment.GetEnvironmentVariable("HERMES_256_CREATEBITS") == "1";

    /// <summary>
    /// TrinityCore's TypeID, which is not the BCC one this repository stores. The modern enum
    /// inserted AzeriteEmpoweredItem and AzeriteItem at 3 and 4, so everything from Unit upwards
    /// shifted by two: an active player is 7 here and 5 in the BCC numbering.
    /// </summary>
    static byte ToModernTypeId(Enums.ObjectTypeBCC type) => type switch
    {
        Enums.ObjectTypeBCC.Object        => 0,
        Enums.ObjectTypeBCC.Item          => 1,
        Enums.ObjectTypeBCC.Container     => 2,
        Enums.ObjectTypeBCC.Unit          => 5,
        Enums.ObjectTypeBCC.Player        => 6,
        Enums.ObjectTypeBCC.ActivePlayer  => 7,
        Enums.ObjectTypeBCC.GameObject    => 8,
        Enums.ObjectTypeBCC.DynamicObject => 9,
        Enums.ObjectTypeBCC.Corpse        => 10,
        Enums.ObjectTypeBCC.AreaTrigger   => 11,
        Enums.ObjectTypeBCC.SceneObject   => 12,
        Enums.ObjectTypeBCC.Conversation  => 13,
        _ => (byte)type,
    };

    public void WriteToPacket(WorldPacket packet)
    {
        var type = m_updateData.Type;
        packet.WriteUInt8((byte)type);
        packet.WritePackedGuid128(m_updateData.Guid);

        bool isCreate = type != Enums.UpdateTypeModern.Values;
        if (isCreate)
        {
            // No int32 HeirFlags here: the 11.x framing dropped it.
            packet.WriteUInt8(ToModernTypeId(m_objectType));
            SetCreateObjectBits();
            BuildMovementUpdate(packet);
        }

        // Everything from here on is length-prefixed, and the length excludes its own four bytes.
        WorldPacket body = new();
        if (isCreate)
        {
            body.WriteUInt8((byte)GetFieldVisibility());
            foreach (var id in GetFragments())
                body.WriteUInt8((byte)id);
            body.WriteUInt8((byte)EntityFragment.End);
            body.WriteUInt8(1);          // CGObject is an indirect fragment: mark it active
        }
        else
        {
            body.WriteUInt8((byte)(GetFieldVisibility().HasFlag(FieldVisibility.Owner) ? 1 : 0));
            body.WriteUInt8(0);          // IdsChanged: the fragment list never changes for us

            // The byte that follows is NOT a two-bit CGObject "active|changed" mask. Read out of the
            // client's own values-update reader sub_244680 (RVA 0x244680), which the block loop
            // sub_23B630 reaches for updateType 0:
            //
            //   u32  size
            //   u8   visibility flags
            //   u8   idsChanged           (0 => skip the fragment-id re-merge)
            //   bits changedFragmentMask  (N bits, N = number of non-Tag updateable fragments)
            //   for each set bit: run that fragment's UPDATE deserialiser
            //
            // N is produced by sub_2DC5AA0, which excludes every Tag_* (registry kind byte == 4).
            // For every unit/player we emit the only qualifying fragment is CGObject, so N == 1 and
            // the mask is a single byte whose bit 0 means "CGObject changed". CGObject's update
            // deserialiser is table 0x5FD9E00[2*0xE0 + 0x40] = RVA 0x24D380 -> vtable+0x1C8, a MODERN
            // UF changes-mask reader (create is the sibling slot vtable+0x1D0). It is NOT the legacy
            // masked-uint32 array that BuildValuesUpdate writes below.
            //
            // So value 3 sets bits 0 and 1: bit 0 fires CGObject's modern reader against a legacy
            // body it cannot parse (it scribbles our mask/value bytes into random descriptor offsets
            // and then resyncs on the declared size at RVA 0x2450E0), and no value update is ever
            // applied - which is why in-combat health never moves. See the report and section 32's
            // note that WriteUpdate in the 11.x model has no flat mask.
            //
            // HERMES_256_VALUESNOOP=1 writes bit 0 == 0 (no fragment changed) and omits the legacy
            // body, so the client parses a well-formed empty update and applies nothing instead of
            // corrupting the entity. It does not restore health updates - that removes the active
            // corruption only.
            //
            // CORRECTION, 23 Aug, from WPP's own reader for this arm
            // (.wpp/WowPacketParserModule.V5_5_0_61735/Parsers/UpdateHandler.cs, lines 84-208):
            // the mask is TWO bits for CGObject, not one. CGObject is an INDIRECT fragment, so it
            // consumes bit[i] = "changed" and bit[i+1] = "the indirect fragment is active", and the
            // client only descends into it when BOTH are set. With CGObject first in our sorted
            // fragment list that is bits 0 and 1, so the byte 3 below was already correct and the
            // paragraph above is wrong about it. What follows the mask is a u32 updateTypeFlag
            // (1 << modern TypeID) and then each flagged descriptor's changes-mask body - which is
            // what ModernValuesUpdate.cs now writes, and what the legacy array never was.
            //
            // HERMES_256_VALUESUPDATE >= 1 takes that path. See the knob's ladder in
            // ModernValuesUpdate.cs.
            // NEVER WRITE 0 HERE. This byte is not "did anything change" - bit 0 is CGObject's
            // changed bit and bit 1 is "the indirect fragment EXISTS", and clearing bit 0 tells the
            // client to tear the fragment down. WPP's reader for this arm is explicit
            // (UpdateHandler.cs line 203):
            //
            //     if (objIdx >= 0 && changedFragments[objIdx]) { ... read ... }
            //     else obj?.EntityFragments.RemoveAll(f => f.UniversalValue == CGObject);
            //
            // TrinityCore never emits 0 either: Object::BuildEntityFragmentsForValuesUpdateForPlayerWithMask
            // (Object.cpp:113) sets the "exists" bit for EVERY indirect updateable fragment and the
            // "changed" bit for CGObject unconditionally, so for our shape the byte is always 3.
            //
            // MEASURED, 23 Aug 03:18. Writing 0 for "nothing our encoder can express changed" put
            // the client one update later into a NULL indirect-fragment constructor and killed it:
            // capture modern_3574_1787447869_1.pkt packet #24 block 2 is `01 00 00` (mask 0, the
            // teardown) and packet #25 - the last in the file - is a player Values with mask 3 and
            // a real body. The client then walked the entity's fragment list at RVA 0x2DC0970,
            // found CGObject still listed with a null storage pointer, and at 0x2DC4939 did
            // `MOV RAX,[RDI+0x78]; CALL RAX` where RDI is the component registry entry for
            // fragment id 2 - and CGObject's +0x78 slot is NULL (fragments 1 and 18 have real
            // constructors there; CGObject's storage is only ever established by a create block).
            // ACCESS_VIOLATION at 0x0, RAX=0, RDI=rva 0x3C5EED0 = 0x3C5ED70 + 2*0xB0.
            //
            // The empty update is mask 3 with updateTypeFlag == 0, which is exactly what
            // Object::BuildValuesUpdateWithFlag (Object.cpp:135) writes: `data << uint32(0)`.
            if (ModernValuesEnabled || m_updateData.ForceApdValuesTest)
            {
                // Build into a scratch buffer first. WriteModernValuesUpdate writes the u32
                // updateTypeFlag itself and returns false only when no descriptor changed, in
                // which case we still write the flag - as a zero - so the block stays a
                // well-formed empty update instead of a fragment teardown.
                WorldPacket modern = new();
                bool any = WriteModernValuesUpdate(modern);
                body.WriteUInt8(3);      // CGObject: changed (bit 0) and exists (bit 1)
                if (any)
                    body.WriteBytes(modern.GetData());
                else
                    body.WriteUInt32(0); // updateTypeFlag: no descriptor changed
            }
            else if (SuppressLegacyValuesBody)
            {
                // VALUESNOOP was introduced as a "clean no-op" and was not one: it wrote the same
                // teardown byte. Give it the real empty form now that the form is known.
                body.WriteUInt8(3);
                body.WriteUInt32(0);
            }
            else
                body.WriteUInt8(3);      // current behaviour: bit 0 fires CGObject's modern reader
        }

        if (isCreate)
        {
            // The 11.x create block writes every descriptor in full, in order, with no mask —
            // see ModernDescriptors.cs. Player order is ObjectData, UnitData, PlayerData and,
            // only when the receiver is the object itself, ActivePlayerData.
            WriteObjectData(body);
            switch (m_objectType)
            {
                case Enums.ObjectTypeBCC.Unit:
                {
                    // Same bisection harness as the ActivePlayer branch below: `creature:<range>`
                    // blanks bytes relative to UnitData's start on a plain world unit, so a value
                    // can be taken off the wire without moving the block boundary.
                    uint oCreature = body.GetSize();
                    WriteUnitData(body);
                    ApplyZeroRanges(body, oCreature, uint.MaxValue, uint.MaxValue, "creature");
                    break;
                }
                case Enums.ObjectTypeBCC.Player:
                    WriteUnitData(body); WritePlayerData(body); break;
                case Enums.ObjectTypeBCC.ActivePlayer:
                {
                    // FIXME(256-spike): diagnostic. The client reads a float where the
                    // TransmogOutfits map count should be, so the ActivePlayer stream is
                    // misaligned. Record each descriptor's byte range and dump the bytes, so the
                    // divergence can be located by offset instead of by reading source.
                    uint oUnit = body.GetSize();
                    WriteUnitData(body);
                    uint oPlayer = body.GetSize();
                    WritePlayerData(body);
                    // GetSize() returns _length, which excludes a pending bit-pack, and
                    // WritePlayerData always ends with WriteBits(0,1) for LeaverStatus. That byte
                    // reaches the wire on the next fixed-width write, so PlayerData is one byte
                    // longer than sampled here and ActivePlayerData starts one byte later. Do NOT
                    // flush instead: on the s_noApd path nothing follows, so a flush would add a
                    // byte to the wire rather than only to the count. This offset also feeds
                    // ApplyZeroRanges, so `ap:` ranges were one byte low.
                    uint oActive = body.GetSize() + (body.HasUnfinishedBitPack() ? 1u : 0u);
                    if (!s_noApd)
                        WriteActivePlayerData(body);
                    uint oEnd = body.GetSize();
                    Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
                        $"[256-spike] descriptor ranges: object=0..{oUnit} unit={oUnit}..{oPlayer} " +
                        $"player={oPlayer}..{oActive} activeplayer={oActive}..{oEnd}");
                    ApplyZeroRanges(body, oUnit, oPlayer, oActive);
                    try
                    {
                        var all = body.GetData();
                        System.IO.File.WriteAllBytes("C:/projekter/hermes2/activeplayer-dump.bin", all);
                        System.IO.File.WriteAllText("C:/projekter/hermes2/activeplayer-dump.txt",
                            $"{oUnit} {oPlayer} {oActive} {oEnd}");
                    }
                    catch { }
                    break;
                }
                case Enums.ObjectTypeBCC.GameObject:
                {
                    uint oGob = body.GetSize();
                    WriteGameObjectData(body);
                    ApplyZeroRanges(body, oGob, uint.MaxValue, uint.MaxValue, "gameobject");
                    break;
                }
                case Enums.ObjectTypeBCC.DynamicObject:
                    WriteDynamicObjectData(body); break;
                case Enums.ObjectTypeBCC.Corpse:
                    WriteCorpseData(body); break;
                case Enums.ObjectTypeBCC.Item:
                    WriteItemData(body); break;
                case Enums.ObjectTypeBCC.Container:
                    WriteItemData(body); WriteContainerData(body); break;
            }
        }
        else if (!ModernValuesEnabled && !SuppressLegacyValuesBody && !m_updateData.ForceApdValuesTest)
        {
            // Current behaviour: the legacy 2.4.3 masked-uint32 array. The client's CGObject update
            // deserialiser (RVA 0x24D380 -> vtable+0x1C8) cannot parse this; with the changed-
            // fragment mask above set to 0 (HERMES_256_VALUESNOOP) this body is omitted and the
            // update becomes a clean no-op instead of corrupting the entity.
            //
            // With HERMES_256_VALUESUPDATE >= 1 the body was already written above by
            // WriteModernValuesUpdate, and this legacy array must NOT also run - only one of the
            // two paths ever writes.
            BuildValuesUpdate(body);
        }

        // Pad the values so the client cannot read past the end of the block.
        //
        // Its parser consumes whatever its own field list demands and then compares the bytes
        // consumed against the size we declared. On a mismatch of 500 bytes or more it does not
        // fail: it sets the stream cursor to startPos + declaredSize and carries on (RVA
        // 0x24526E). So a field list that does not match ours is survivable — as long as it never
        // runs off the end while reading, which is the ACCESS_VIOLATION we were getting.
        //
        // Padding costs bandwidth on a loopback socket and nothing else.
        // Pad the values. The client's parser consumes what its own field list demands and only
        // compares against our declared size afterwards (RVA 0x2450E0), resyncing to
        // startPos + declaredSize on a mismatch of 500 bytes or more. So a field list that differs
        // from ours is survivable, but reading past the end of the block is not.
        // Zero tail for create blocks. This is a workaround, not a fix, and it is here on evidence:
        // a dump of the ActivePlayer create block is 99% zeroes and contains no 0x40000000
        // anywhere, yet the client allocated for a count of 0x40000000 while reading the
        // TransmogOutfits map. It therefore read that value from past the end of our data. This
        // build's descriptor is longer than CypherCore 11.2.5's, which we generated from, so the
        // client keeps reading after we stop. Zeroes give it empty counts to find instead of
        // whatever the receive buffer last held.
        //
        // The real fix is to extract this build's field list from the client's own reader (the
        // tail of which is at RVA 0x71ABBF) and emit the missing fields. Until then the tail keeps
        // the parse in sync; the client resyncs on the declared block size afterwards.
        // Tunable while the right length is being searched for: HERMES_256_PAD sets the tail in
        // bytes, so a candidate can be tried by restarting the proxy rather than rebuilding it.
        // 0 leaves the client reading past the end of our data; 16384 makes it reject the packet
        // outright. The correct value is whatever makes the client's field count come out even.
        if (isCreate && s_createPad > 0)
        {
            // HERMES_256_SENTINEL fills the tail with self-locating values instead of zeroes: every
            // 4-byte slot holds its own offset from the start of the block. If the client reads past
            // the end of our data, whatever it reports back — an allocation size, a bogus count —
            // is literally the offset its cursor had reached, which turns "somewhere past the end"
            // into an exact byte position in one run.
            var tail = new byte[s_createPad];
            if (s_sentinel)
            {
                // Bias the offset into the range where an allocation is refused and REPORTED.
                // A bare offset (a few thousand) is a plausible count: the client loops on it for a
                // minute and dies of a watchdog freeze, which reports no size and tells us nothing.
                // 0x40000000 + offset is large enough that the allocator gives up and prints the
                // byte count, and the offset is recoverable as (reported / 32) - 0x40000000.
                uint at = body.GetSize();
                for (int i = 0; i + 4 <= tail.Length; i += 4)
                    BitConverter.TryWriteBytes(tail.AsSpan(i, 4), 0x40000000u + at + (uint)i);
            }
            body.WriteBytes(tail);
        }

        var bytes = body.GetData();

        // FIXME(256-spike): diagnostic. Dumps the active player's block header so the fragment list
        // and the bytes around it are visible; the packet log truncates well before it.
        if (isCreate)
        {
            Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
                $"[256-spike] block: type={m_objectType} guid={m_updateData.Guid} " +
                $"values={bytes.Length} packetSoFar={packet.GetData().Length}");
        }

        // Diagnostic: what the legacy server actually gave us for a creature — every NPC showing
        // 100 health and neutrals being unattackable both point at these three fields.
        if (isCreate && m_objectType == Enums.ObjectTypeBCC.Unit)
        {
            var u = m_updateData.UnitData;
            Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
                $"[256-spike] unit-create: guid={m_updateData.Guid} hp={u?.Health}/{u?.MaxHealth} " +
                $"lvl={u?.Level} faction={u?.FactionTemplate} flags={u?.Flags} npcFlags={u?.NpcFlags[0]} " +
                $"displayId={u?.DisplayID}");
        }

        // Diagnostic: Owner/ContainedIn/StackCount for every 69110 item create, so a relog create
        // and a live split/move create can be compared for the same item guid.
        if (isCreate && (m_objectType == Enums.ObjectTypeBCC.Item || m_objectType == Enums.ObjectTypeBCC.Container))
        {
            var it = m_updateData.ItemData;
            Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
                $"[256-spike] item-create: guid={m_updateData.Guid} owner={it?.Owner} " +
                $"containedIn={it?.ContainedIn} stack={it?.StackCount}");
        }

        if (isCreate && m_objectType == Enums.ObjectTypeBCC.ActivePlayer)
        {
            // Log the packet from the start of this block, so the movement section is visible and
            // its length can be counted against CypherCore's layout. `packet` already holds
            // updateType, guid, objectType and the movement block at this point.
            var whole = packet.GetData();
            int from = System.Math.Max(0, whole.Length - 0);
            Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
                $"[256-spike] header+movement ({whole.Length} bytes): " +
                System.Convert.ToHexString(whole, 0, System.Math.Min(760, whole.Length)));
            var ud = m_updateData.UnitData;
            var pd = m_updateData.PlayerData;
            Framework.Logging.Log.Print(Framework.Logging.LogType.Warn,
                $"[256-spike] unit: display={ud?.DisplayID} native={ud?.NativeDisplayID} " +
                $"race={ud?.RaceId} class={ud?.ClassId} sex={ud?.SexId} lvl={ud?.Level} " +
                $"hp={ud?.Health}/{ud?.MaxHealth} faction={ud?.FactionTemplate} " +
                $"bound={ud?.BoundingRadius} reach={ud?.CombatReach} " +
                $"playerSex={pd?.NativeSex} customCount={pd?.Customizations?.Length}");
        }

        packet.WriteUInt32((uint)bytes.Length);
        packet.WriteBytes(bytes);
    }

    public void SetCreateObjectBits()
    {
        var createData = m_updateData.CreateData;
        var moveInfo = createData?.MoveInfo;
        bool isGameObject = m_objectType == Enums.ObjectTypeBCC.GameObject;
        bool isUnit = m_objectTypeMask.HasAnyFlag(Enums.ObjectTypeMask.Unit);
        bool isActivePlayer = m_objectType == Enums.ObjectTypeBCC.ActivePlayer;

        m_createBits =
            (moveInfo is { Hover: true }                                        ? CreateObjectBits.PlayHoverAnim     : 0) |
            (moveInfo != null && isUnit                                          ? CreateObjectBits.MovementUpdate    : 0) |
            (moveInfo != null && moveInfo.TransportGuid != default && isGameObject ? CreateObjectBits.MovementTransport : 0) |
            (moveInfo != null && !isUnit                                         ? CreateObjectBits.Stationary        : 0) |
            (moveInfo != null && m_updateData.Guid.GetHighType() == Enums.HighGuidType.Transport ? CreateObjectBits.ServerTime : 0) |
            (createData?.AutoAttackVictim != null                                ? CreateObjectBits.CombatVictim      : 0) |
            (moveInfo is { VehicleId: not 0 }                                    ? CreateObjectBits.Vehicle           : 0) |
            (moveInfo != null && isGameObject                                    ? CreateObjectBits.Rotation          : 0) |
            (isActivePlayer                                                      ? CreateObjectBits.ThisIsYou | CreateObjectBits.ActivePlayer : 0);
    }

    public void BuildValuesUpdate(WorldPacket packet)
    {
        WriteValuesToArray();
        m_fields.WriteToPacket(packet);
    }

    public void BuildDynamicValuesUpdate(WorldPacket packet)
    {
        m_dynamicFields.WriteToPacket(packet);
    }

    /// <summary>
    /// The 11.x create-object flags: 21 bits, not 18, and reordered. HasEntityPosition is new and
    /// first, ThisIsYou moved from bit 14 to bit 4, AreaTrigger is gone, and Room/Decor/MeshObject
    /// were appended. The old and new forms both occupy three bytes, so writing the old one is not
    /// a length error — the client simply reads MovementUpdate and Stationary from the wrong
    /// positions, parses a different structure, and everything after the movement block shifts.
    /// </summary>
    void WriteCreateBitsModern(WorldPacket data)
    {
        var b = m_createBits;
        data.WriteBit(!s_createBits || m_updateData.CreateData?.MoveInfo != null); // HasEntityPosition - live: set only for objects that HAVE a position (items: clear)
        data.WriteBit(b.HasFlag(CreateObjectBits.NoBirthAnim));
        data.WriteBit(b.HasFlag(CreateObjectBits.EnablePortals));
        data.WriteBit(b.HasFlag(CreateObjectBits.PlayHoverAnim));
        data.WriteBit(b.HasFlag(CreateObjectBits.ThisIsYou));
        data.WriteBit(b.HasFlag(CreateObjectBits.MovementUpdate));
        data.WriteBit(b.HasFlag(CreateObjectBits.MovementTransport));
        data.WriteBit(b.HasFlag(CreateObjectBits.Stationary));
        data.WriteBit(b.HasFlag(CreateObjectBits.CombatVictim));
        data.WriteBit(b.HasFlag(CreateObjectBits.ServerTime));
        data.WriteBit(b.HasFlag(CreateObjectBits.Vehicle));
        data.WriteBit(b.HasFlag(CreateObjectBits.AnimKit));
        data.WriteBit(b.HasFlag(CreateObjectBits.Rotation));
        if (s_createBits)
            data.WriteBit(false);                              // AreaTrigger - NOT gone on this build: live puts GameObject at 14 and ActivePlayer at 17
        data.WriteBit(b.HasFlag(CreateObjectBits.GameObject));
        data.WriteBit(b.HasFlag(CreateObjectBits.SmoothPhasing));
        data.WriteBit(b.HasFlag(CreateObjectBits.SceneObject));
        data.WriteBit(b.HasFlag(CreateObjectBits.ActivePlayer));
        data.WriteBit(b.HasFlag(CreateObjectBits.Conversation));
        data.WriteBit(false);                                      // Room
        data.WriteBit(false);                                      // Decor
        data.WriteBit(false);                                      // MeshObject
        data.FlushBits();
    }

    public void BuildMovementUpdate(WorldPacket data)
    {
        int PauseTimesCount = 0;

        WriteCreateBitsModern(data);

        if (m_createBits.HasFlag(CreateObjectBits.MovementUpdate))
        {
            MovementInfo moveInfo = m_updateData.CreateData.MoveInfo;
            // BISECT: no spline for the player's own object. It accounts for 525 of the 715 bytes
            // ahead of the values, and a logging-in player has no business having one — CypherCore
            // never sends a spline from SendInitSelf. If the crash moves, the spline block's
            // 5.5.0 layout is the remaining problem.
            // BISECT: no spline on this engine at all. The player's block now clears the fragment
            // stage while creatures still fail there, and the spline block is the only remaining
            // structural difference between them.
            bool hasSpline = m_updateData.CreateData.MoveSpline != null
                && (MovementInfo.SendSplines || !ModernVersion.Uses550Engine);
            moveInfo.WriteMovementInfoModern(data, m_updateData.Guid);

            data.WriteFloat(moveInfo.WalkSpeed);
            data.WriteFloat(moveInfo.RunSpeed);
            data.WriteFloat(moveInfo.RunBackSpeed);
            data.WriteFloat(moveInfo.SwimSpeed);
            data.WriteFloat(moveInfo.SwimBackSpeed);
            data.WriteFloat(moveInfo.FlightSpeed);
            data.WriteFloat(moveInfo.FlightBackSpeed);
            data.WriteFloat(moveInfo.TurnRate);
            data.WriteFloat(moveInfo.PitchRate);

            //MovementForces movementForces = unit.GetMovementForces();
            //if (movementForces != null)
            //{
            //    data.WriteInt32(movementForces.GetForces().Count);
            //    data.WriteFloat(movementForces.GetModMagnitude());          // MovementForcesModMagnitude
            //}
            //else
            //{
                data.WriteUInt32(0);
                data.WriteFloat(1.0f);                                       // MovementForcesModMagnitude
            //}

            // The 5.5.0 engine added seventeen advanced-flying floats here, between the movement
            // forces and the spline bit — see CypherCore's BaseEntity.BuildMovementUpdate. They sit
            // inside the movement block, which is written *before* the length-prefixed values, so
            // omitting them left everything after this point sixty-eight bytes out of place: the
            // fragment list, the indirect byte and the whole values payload. The client then read a
            // component id out of the middle of our data, which is where the phantom id 0 came from.
            if (ModernVersion.Uses550Engine)
            {
                data.WriteFloat(0.0f);      // AirFriction
                data.WriteFloat(0.0f);      // MaxVel
                data.WriteFloat(0.0f);      // LiftCoefficient
                data.WriteFloat(0.0f);      // DoubleJumpVelMod
                data.WriteFloat(0.0f);      // GlideStartMinHeight
                data.WriteFloat(0.0f);      // AddImpulseMaxSpeed
                data.WriteFloat(0.0f);      // BankingRate min
                data.WriteFloat(0.0f);      // BankingRate max
                data.WriteFloat(0.0f);      // PitchingRateDown min
                data.WriteFloat(0.0f);      // PitchingRateDown max
                data.WriteFloat(0.0f);      // PitchingRateUp min
                data.WriteFloat(0.0f);      // PitchingRateUp max
                data.WriteFloat(0.0f);      // TurnVelocityThreshold min
                data.WriteFloat(0.0f);      // TurnVelocityThreshold max
                data.WriteFloat(0.0f);      // SurfaceFriction
                data.WriteFloat(0.0f);      // OverMaxDeceleration
                data.WriteFloat(0.0f);      // LaunchSpeedCoefficient
            }

            data.WriteBit(hasSpline);
            data.FlushBits();

            //if (movementForces != null)
            //    foreach (MovementForce force in movementForces.GetForces())
            //        MovementExtensions.WriteMovementForceWithDirection(force, data, unit);

            // HasMovementSpline - marks that spline data is present in packet
            if (hasSpline)
                WriteCreateObjectSplineDataBlock(m_updateData.CreateData.MoveSpline!, data);
        }

        data.WriteInt32(PauseTimesCount);

        if (m_createBits.HasFlag(CreateObjectBits.Stationary))
        {
            data.WriteFloat(m_updateData.CreateData.MoveInfo.Position.X);
            data.WriteFloat(m_updateData.CreateData.MoveInfo.Position.Y);
            data.WriteFloat(m_updateData.CreateData.MoveInfo.Position.Z);
            data.WriteFloat(m_updateData.CreateData.MoveInfo.Orientation);
        }

        if (m_createBits.HasFlag(CreateObjectBits.CombatVictim))
            data.WritePackedGuid128(m_updateData.CreateData.AutoAttackVictim!.Value); // CombatVictim

        if (m_createBits.HasFlag(CreateObjectBits.ServerTime))
        {
            /** @TODO Use IsTransport() to also handle type 11 (TRANSPORT)
                Currently grid objects are not updated if there are no nearby players,
                this causes clients to receive different PathProgress
                resulting in players seeing the object in a different position
            */
            if (m_updateData.CreateData.MoveInfo.TransportPathTimer != 0) // ServerTime
                data.WriteUInt32(m_updateData.CreateData.MoveInfo.TransportPathTimer);
            else
                data.WriteUInt32((uint)Time.UnixTime);
        }

        if (m_createBits.HasFlag(CreateObjectBits.Vehicle))
        {
            data.WriteUInt32(m_updateData.CreateData.MoveInfo.VehicleId); // RecID
            data.WriteFloat(m_updateData.CreateData.MoveInfo.VehicleOrientation); // InitialRawFacing
        }

        if (m_createBits.HasFlag(CreateObjectBits.AnimKit))
        {
            data.WriteUInt16(0); // AiID
            data.WriteUInt16(0); // MovementID
            data.WriteUInt16(0); // MeleeID
        }

        if (m_createBits.HasFlag(CreateObjectBits.Rotation))
            data.WriteInt64(m_updateData.CreateData.MoveInfo.Rotation.GetPackedRotation()); // Rotation

        for (int i = 0; i < PauseTimesCount; ++i)
            data.WriteUInt32(0);

        if (m_createBits.HasFlag(CreateObjectBits.MovementTransport))
            m_updateData.CreateData.MoveInfo.WriteTransportInfoModern(data);

        /*
        if (m_createBits.HasFlag(CreateObjectBits.AreaTrigger))
        {
            AreaTrigger areaTrigger = ToAreaTrigger();
            AreaTriggerMiscTemplate areaTriggerMiscTemplate = areaTrigger.GetMiscTemplate();
            AreaTriggerTemplate areaTriggerTemplate = areaTrigger.GetTemplate();

            data.WriteUInt32(areaTrigger.GetTimeSinceCreated());

            data.WriteVector3(areaTrigger.GetRollPitchYaw());

            bool hasAbsoluteOrientation = areaTriggerTemplate.HasFlag(AreaTriggerFlags.HasAbsoluteOrientation);
            bool hasDynamicShape = areaTriggerTemplate.HasFlag(AreaTriggerFlags.HasDynamicShape);
            bool hasAttached = areaTriggerTemplate.HasFlag(AreaTriggerFlags.HasAttached);
            bool hasFaceMovementDir = areaTriggerTemplate.HasFlag(AreaTriggerFlags.HasFaceMovementDir);
            bool hasFollowsTerrain = areaTriggerTemplate.HasFlag(AreaTriggerFlags.HasFollowsTerrain);
            bool hasUnk1 = areaTriggerTemplate.HasFlag(AreaTriggerFlags.Unk1);
            bool hasTargetRollPitchYaw = areaTriggerTemplate.HasFlag(AreaTriggerFlags.HasTargetRollPitchYaw);
            bool hasScaleCurveID = areaTriggerMiscTemplate.ScaleCurveId != 0;
            bool hasMorphCurveID = areaTriggerMiscTemplate.MorphCurveId != 0;
            bool hasFacingCurveID = areaTriggerMiscTemplate.FacingCurveId != 0;
            bool hasMoveCurveID = areaTriggerMiscTemplate.MoveCurveId != 0;
            bool hasAnimation = areaTriggerTemplate.HasFlag(AreaTriggerFlags.HasAnimID);
            bool hasUnk3 = areaTriggerTemplate.HasFlag(AreaTriggerFlags.Unk3);
            bool hasAnimKitID = areaTriggerTemplate.HasFlag(AreaTriggerFlags.HasAnimKitID);
            bool hasAnimProgress = false;
            bool hasAreaTriggerSphere = areaTriggerTemplate.IsSphere();
            bool hasAreaTriggerBox = areaTriggerTemplate.IsBox();
            bool hasAreaTriggerPolygon = areaTriggerTemplate.IsPolygon();
            bool hasAreaTriggerCylinder = areaTriggerTemplate.IsCylinder();
            bool hasAreaTriggerSpline = areaTrigger.HasSplines();
            bool hasOrbit = areaTrigger.HasOrbit();
            bool hasMovementScript = false;

            data.WriteBit(hasAbsoluteOrientation);
            data.WriteBit(hasDynamicShape);
            data.WriteBit(hasAttached);
            data.WriteBit(hasFaceMovementDir);
            data.WriteBit(hasFollowsTerrain);
            data.WriteBit(hasUnk1);
            data.WriteBit(hasTargetRollPitchYaw);
            data.WriteBit(hasScaleCurveID);
            data.WriteBit(hasMorphCurveID);
            data.WriteBit(hasFacingCurveID);
            data.WriteBit(hasMoveCurveID);
            data.WriteBit(hasAnimation);
            data.WriteBit(hasAnimKitID);
            data.WriteBit(hasUnk3);
            data.WriteBit(hasAnimProgress);
            data.WriteBit(hasAreaTriggerSphere);
            data.WriteBit(hasAreaTriggerBox);
            data.WriteBit(hasAreaTriggerPolygon);
            data.WriteBit(hasAreaTriggerCylinder);
            data.WriteBit(hasAreaTriggerSpline);
            data.WriteBit(hasOrbit);
            data.WriteBit(hasMovementScript);

            if (hasUnk3)
                data.WriteBit(false);

            data.FlushBits();

            if (hasAreaTriggerSpline)
            {
                data.WriteUInt32(areaTrigger.GetTimeToTarget());
                data.WriteUInt32(areaTrigger.GetElapsedTimeForMovement());

                MovementExtensions.WriteCreateObjectAreaTriggerSpline(areaTrigger.GetSpline(), data);
            }

            if (hasTargetRollPitchYaw)
                data.WriteVector3(areaTrigger.GetTargetRollPitchYaw());

            if (hasScaleCurveID)
                data.WriteUInt32(areaTriggerMiscTemplate.ScaleCurveId);

            if (hasMorphCurveID)
                data.WriteUInt32(areaTriggerMiscTemplate.MorphCurveId);

            if (hasFacingCurveID)
                data.WriteUInt32(areaTriggerMiscTemplate.FacingCurveId);

            if (hasMoveCurveID)
                data.WriteUInt32(areaTriggerMiscTemplate.MoveCurveId);

            if (hasAnimation)
                data.WriteUInt32(areaTriggerMiscTemplate.AnimId);

            if (hasAnimKitID)
                data.WriteUInt32(areaTriggerMiscTemplate.AnimKitId);

            if (hasAnimProgress)
                data.WriteUInt32(0);

            if (hasAreaTriggerSphere)
            {
                data.WriteFloat(areaTriggerTemplate.SphereDatas.Radius);
                data.WriteFloat(areaTriggerTemplate.SphereDatas.RadiusTarget);
            }

            if (hasAreaTriggerBox)
            {
                unsafe
                {
                    data.WriteFloat(areaTriggerTemplate.BoxDatas.Extents[0]);
                    data.WriteFloat(areaTriggerTemplate.BoxDatas.Extents[1]);
                    data.WriteFloat(areaTriggerTemplate.BoxDatas.Extents[2]);

                    data.WriteFloat(areaTriggerTemplate.BoxDatas.ExtentsTarget[0]);
                    data.WriteFloat(areaTriggerTemplate.BoxDatas.ExtentsTarget[1]);
                    data.WriteFloat(areaTriggerTemplate.BoxDatas.ExtentsTarget[2]);
                }
            }

            if (hasAreaTriggerPolygon)
            {
                data.WriteInt32(areaTriggerTemplate.PolygonVertices.Count);
                data.WriteInt32(areaTriggerTemplate.PolygonVerticesTarget.Count);
                data.WriteFloat(areaTriggerTemplate.PolygonDatas.Height);
                data.WriteFloat(areaTriggerTemplate.PolygonDatas.HeightTarget);

                foreach (var vertice in areaTriggerTemplate.PolygonVertices)
                    data.WriteVector2(vertice);

                foreach (var vertice in areaTriggerTemplate.PolygonVerticesTarget)
                    data.WriteVector2(vertice);
            }

            if (hasAreaTriggerCylinder)
            {
                data.WriteFloat(areaTriggerTemplate.CylinderDatas.Radius);
                data.WriteFloat(areaTriggerTemplate.CylinderDatas.RadiusTarget);
                data.WriteFloat(areaTriggerTemplate.CylinderDatas.Height);
                data.WriteFloat(areaTriggerTemplate.CylinderDatas.HeightTarget);
                data.WriteFloat(areaTriggerTemplate.CylinderDatas.LocationZOffset);
                data.WriteFloat(areaTriggerTemplate.CylinderDatas.LocationZOffsetTarget);
            }

            //if (hasMovementScript)
            //    *data << *areaTrigger->GetMovementScript(); // AreaTriggerMovementScriptInfo

            if (hasOrbit)
                areaTrigger.GetCircularMovementInfo().Value.Write(data);
        }
        */

        if (m_createBits.HasFlag(CreateObjectBits.GameObject))
        {
            bool bit8 = false;
            uint Int1 = 0;

            data.WriteUInt32(0); // WorldEffectID

            data.WriteBit(bit8);
            data.FlushBits();
            if (bit8)
                data.WriteUInt32(Int1);
        }

        //if (m_createBits.HasFlag(CreateObjectBits.SmoothPhasing))
        //{
        //    data.WriteBit(ReplaceActive);
        //    data.WriteBit(StopAnimKits);
        //    data.WriteBit(HasReplaceObjectt);
        //    data.FlushBits();
        //    if (HasReplaceObject)
        //        *data << ObjectGuid(ReplaceObject);
        //}

        //if (m_createBits.HasFlag(CreateObjectBits.SceneObject))
        //{
        //    data.WriteBit(HasLocalScriptData);
        //    data.WriteBit(HasPetBattleFullUpdate);
        //    data.FlushBits();

        //    if (HasLocalScriptData)
        //    {
        //        data.WriteBits(Data.length(), 7);
        //        data.FlushBits();
        //        data.WriteString(Data);
        //    }

        //    if (HasPetBattleFullUpdate)
        //    {
        //        for (std::size_t i = 0; i < 2; ++i)
        //        {
        //            *data << ObjectGuid(Players[i].CharacterID);
        //            *data << int32(Players[i].TrapAbilityID);
        //            *data << int32(Players[i].TrapStatus);
        //            *data << uint16(Players[i].RoundTimeSecs);
        //            *data << int8(Players[i].FrontPet);
        //            *data << uint8(Players[i].InputFlags);

        //            data.WriteBits(Players[i].Pets.size(), 2);
        //            data.FlushBits();
        //            for (std::size_t j = 0; j < Players[i].Pets.size(); ++j)
        //            {
        //                *data << ObjectGuid(Players[i].Pets[j].BattlePetGUID);
        //                *data << int32(Players[i].Pets[j].SpeciesID);
        //                *data << int32(Players[i].Pets[j].DisplayID);
        //                *data << int32(Players[i].Pets[j].CollarID);
        //                *data << int16(Players[i].Pets[j].Level);
        //                *data << int16(Players[i].Pets[j].Xp);
        //                *data << int32(Players[i].Pets[j].CurHealth);
        //                *data << int32(Players[i].Pets[j].MaxHealth);
        //                *data << int32(Players[i].Pets[j].Power);
        //                *data << int32(Players[i].Pets[j].Speed);
        //                *data << int32(Players[i].Pets[j].NpcTeamMemberID);
        //                *data << uint16(Players[i].Pets[j].BreedQuality);
        //                *data << uint16(Players[i].Pets[j].StatusFlags);
        //                *data << int8(Players[i].Pets[j].Slot);

        //                *data << uint32(Players[i].Pets[j].Abilities.size());
        //                *data << uint32(Players[i].Pets[j].Auras.size());
        //                *data << uint32(Players[i].Pets[j].States.size());
        //                for (std::size_t k = 0; k < Players[i].Pets[j].Abilities.size(); ++k)
        //                {
        //                    *data << int32(Players[i].Pets[j].Abilities[k].AbilityID);
        //                    *data << int16(Players[i].Pets[j].Abilities[k].CooldownRemaining);
        //                    *data << int16(Players[i].Pets[j].Abilities[k].LockdownRemaining);
        //                    *data << int8(Players[i].Pets[j].Abilities[k].AbilityIndex);
        //                    *data << uint8(Players[i].Pets[j].Abilities[k].Pboid);
        //                }

        //                for (std::size_t k = 0; k < Players[i].Pets[j].Auras.size(); ++k)
        //                {
        //                    *data << int32(Players[i].Pets[j].Auras[k].AbilityID);
        //                    *data << uint32(Players[i].Pets[j].Auras[k].InstanceID);
        //                    *data << int32(Players[i].Pets[j].Auras[k].RoundsRemaining);
        //                    *data << int32(Players[i].Pets[j].Auras[k].CurrentRound);
        //                    *data << uint8(Players[i].Pets[j].Auras[k].CasterPBOID);
        //                }

        //                for (std::size_t k = 0; k < Players[i].Pets[j].States.size(); ++k)
        //                {
        //                    *data << uint32(Players[i].Pets[j].States[k].StateID);
        //                    *data << int32(Players[i].Pets[j].States[k].StateValue);
        //                }

        //                data.WriteBits(Players[i].Pets[j].CustomName.length(), 7);
        //                data.FlushBits();
        //                data.WriteString(Players[i].Pets[j].CustomName);
        //            }
        //        }

        //        for (std::size_t i = 0; i < 3; ++i)
        //        {
        //            *data << uint32(Enviros[j].Auras.size());
        //            *data << uint32(Enviros[j].States.size());
        //            for (std::size_t j = 0; j < Enviros[j].Auras.size(); ++j)
        //            {
        //                *data << int32(Enviros[j].Auras[j].AbilityID);
        //                *data << uint32(Enviros[j].Auras[j].InstanceID);
        //                *data << int32(Enviros[j].Auras[j].RoundsRemaining);
        //                *data << int32(Enviros[j].Auras[j].CurrentRound);
        //                *data << uint8(Enviros[j].Auras[j].CasterPBOID);
        //            }

        //            for (std::size_t j = 0; j < Enviros[j].States.size(); ++j)
        //            {
        //                *data << uint32(Enviros[i].States[j].StateID);
        //                *data << int32(Enviros[i].States[j].StateValue);
        //            }
        //        }

        //        *data << uint16(WaitingForFrontPetsMaxSecs);
        //        *data << uint16(PvpMaxRoundTime);
        //        *data << int32(CurRound);
        //        *data << uint32(NpcCreatureID);
        //        *data << uint32(NpcDisplayID);
        //        *data << int8(CurPetBattleState);
        //        *data << uint8(ForfeitPenalty);
        //        *data << ObjectGuid(InitialWildPetGUID);
        //        data.WriteBit(IsPVP);
        //        data.WriteBit(CanAwardXP);
        //        data.FlushBits();
        //    }
        //}

        if (m_createBits.HasFlag(CreateObjectBits.ActivePlayer))
        {
            bool hasSceneInstanceIDs = false;
            bool hasRuneState = false;
            // The 5.5.0 engine writes only two bits here — HasSceneInstanceIDs and HasRuneState —
            // and carries no action buttons in the movement block at all; they travel in
            // SMSG_ACTION_BUTTONS instead. See CypherCore's BuildMovementUpdate.
            //
            // Sending them anyway added one bit plus 132 action buttons: 1 + 132*4 = 529 bytes,
            // which is exactly how far the values size field and the entity-fragment list were
            // displaced. The client read its fragment ids out of the middle of the action buttons,
            // which is why it always ended up with the empty component id 0 no matter what we put
            // in the block.
            bool hasActionButtons = !ModernVersion.Uses550Engine
                && m_gameState.ActionButtons.Count != 0;

            data.WriteBit(hasSceneInstanceIDs);
            data.WriteBit(hasRuneState);
            if (!ModernVersion.Uses550Engine)
                data.WriteBit(hasActionButtons);
            data.FlushBits();

            if (hasSceneInstanceIDs)
            {
                var sceneInstanceIDs = 0;
                data.WriteInt32(sceneInstanceIDs);
                for (var i = 0; i < sceneInstanceIDs; ++i)
                    data.WriteInt32(0); // SceneInstanceIDs
            }

            if (hasRuneState)
            {
                byte RechargingRuneMask = 0;
                byte UsableRuneMask = 0;
                data.WriteUInt8(RechargingRuneMask);
                data.WriteUInt8(UsableRuneMask);

                uint runeCount = 0;
                data.WriteUInt32(runeCount);
                for (var i = 0; i < runeCount; ++i)
                    data.WriteUInt8(0); // RuneCooldown
            }

            if (hasActionButtons)
            {
                for (int i = 0; i < 132; i++)
                    data.WriteInt32(m_gameState.ActionButtons[i]);
            }
        }

        /*
        if (m_createBits.HasFlag(CreateObjectBits.Conversation))
        {
            Conversation self = ToConversation();
            if (data.WriteBit(self.GetTextureKitId() != 0))
                data.WriteUInt32(self.GetTextureKitId());
            data.FlushBits();
        }
        */
    }

    public static void WriteCreateObjectSplineDataBlock(ServerSideMovement moveSpline, WorldPacket data)
    {
        data.WriteUInt32(moveSpline.SplineId);                                          // ID

        if (!moveSpline.SplineFlags.HasAnyFlag(Enums.SplineFlagModern.Cyclic))          // Destination
            data.WriteVector3(moveSpline.EndPosition);
        else
            data.WriteVector3(Vector3.Zero);

        bool hasSplineMove = data.WriteBit(moveSpline.SplineCount != 0);
        data.FlushBits();

        if (hasSplineMove)
        {
            data.WriteUInt32((uint)moveSpline.SplineFlags);                             // SplineFlags
            data.WriteUInt32(moveSpline.SplineTime);                                    // Elapsed
            data.WriteUInt32(moveSpline.SplineTimeFull);                                // Duration
            data.WriteFloat(1.0f);                                                      // DurationModifier
            data.WriteFloat(1.0f);                                                      // NextDurationModifier
            data.WriteBits((byte)moveSpline.SplineType, 2);                             // Face
            bool hasFadeObjectTime = data.WriteBit(false);
            data.WriteBits(moveSpline.SplineCount, 16);
            data.WriteBit(false);                                                       // HasSplineFilter
            data.WriteBit(false);                                                       // HasSpellEffectExtraData
            data.WriteBit(false);                                                       // HasJumpExtraData
            // The 5.5.0 engine added HasTurnData before the animation-tier bit and
            // HasSpellVisualData after it — see CypherCore's WriteCreateObjectSplineDataBlock.
            // Two missing bits here shift the rest of the spline block, and with it the values
            // size field and the entity-fragment list that follow.
            if (ModernVersion.Uses550Engine)
                data.WriteBit(false);                                                   // HasTurnData
            data.WriteBit(false);                                                       // HasAnimationTierTransition
            if (ModernVersion.Uses550Engine)
                data.WriteBit(false);                                                   // HasSpellVisualData
            data.FlushBits();

            //if (HasSplineFilterKey)
            //{
            //    data << uint32(FilterKeysCount);
            //    for (var i = 0; i < FilterKeysCount; ++i)
            //    {
            //        data << float(In);
            //        data << float(Out);
            //    }

            //    data.WriteBits(FilterFlags, 2);
            //    data.FlushBits();
            //}

            switch (moveSpline.SplineType)
            {
                case Enums.SplineTypeModern.FacingSpot:
                    data.WriteVector3(moveSpline.FinalFacingSpot);  // FaceSpot
                    break;
                case Enums.SplineTypeModern.FacingTarget:
                    data.WritePackedGuid128(moveSpline.FinalFacingGuid); // FaceGUID
                    break;
                case Enums.SplineTypeModern.FacingAngle:
                    data.WriteFloat(moveSpline.FinalOrientation);   // FaceDirection
                    break;
            }

            if (hasFadeObjectTime)
                data.WriteInt32(0); // FadeObjectTime

            foreach (var vec in moveSpline.SplinePoints)
                data.WriteVector3(vec);

            /*
            if (moveSpline.spell_effect_extra.HasValue)
            {
                data.WritePackedGuid(moveSpline.spell_effect_extra.Value.Target);
                data.WriteUInt32(moveSpline.spell_effect_extra.Value.SpellVisualId);
                data.WriteUInt32(moveSpline.spell_effect_extra.Value.ProgressCurveId);
                data.WriteUInt32(moveSpline.spell_effect_extra.Value.ParabolicCurveId);
            }
            
            if (moveSpline.splineflags.HasFlag(SplineFlag.Parabolic))
            {
                data.WriteFloat(moveSpline.vertical_acceleration);
                data.WriteInt32(moveSpline.effect_start_time);
                data.WriteUInt32(0);                                                  // Duration (override)
            }

            if (moveSpline.anim_tier.HasValue)
            {
                data.WriteUInt32(moveSpline.anim_tier.Value.TierTransitionId);
                data.WriteInt32(moveSpline.effect_start_time);
                data.WriteUInt32(0);
                data.WriteUInt8(moveSpline.anim_tier.Value.AnimTier);
            }*/
        }
    }

    public void WriteValuesToArray()
    {
        if (m_alreadyWritten)
            return;

        ObjectData objectData = m_updateData.ObjectData;
        if (objectData.Guid != default)
            m_fields.SetUpdateField(ObjectField.OBJECT_FIELD_GUID, objectData.Guid);
        if (objectData.EntryID != null)
            m_fields.SetUpdateField<int>(ObjectField.OBJECT_FIELD_ENTRY, (int)objectData.EntryID);
        if (objectData.DynamicFlags != null)
            m_fields.SetUpdateField<uint>(ObjectField.OBJECT_DYNAMIC_FLAGS, (uint)objectData.DynamicFlags);
        if (objectData.Scale != null)
            m_fields.SetUpdateField<float>(ObjectField.OBJECT_FIELD_SCALE_X, (float)objectData.Scale);

        ItemData itemData = m_updateData.ItemData;
        if (itemData != null)
        {
            if (itemData.Owner != null)
                m_fields.SetUpdateField(ItemField.ITEM_FIELD_OWNER, itemData.Owner.Value);
            if (itemData.ContainedIn != null)
                m_fields.SetUpdateField(ItemField.ITEM_FIELD_CONTAINED, itemData.ContainedIn.Value);
            if (itemData.Creator != null)
                m_fields.SetUpdateField(ItemField.ITEM_FIELD_CREATOR, itemData.Creator.Value);
            if (itemData.GiftCreator != null)
                m_fields.SetUpdateField(ItemField.ITEM_FIELD_GIFTCREATOR, itemData.GiftCreator.Value);
            if (itemData.StackCount != null)
                m_fields.SetUpdateField<uint>(ItemField.ITEM_FIELD_STACK_COUNT, (uint)itemData.StackCount);
            if (itemData.Duration != null)
                m_fields.SetUpdateField<uint>(ItemField.ITEM_FIELD_DURATION, (uint)itemData.Duration);
            for (int i = 0; i < 5; i++)
            {
                int startIndex = (int)ItemField.ITEM_FIELD_SPELL_CHARGES;
                if (itemData.SpellCharges[i] != null)
                    m_fields.SetUpdateField<int>(startIndex + i, (int)itemData.SpellCharges[i]!);
            }
            if (itemData.Flags != null)
                m_fields.SetUpdateField<uint>(ItemField.ITEM_FIELD_FLAGS, (uint)itemData.Flags);
            for (int i = 0; i < 13; i++)
            {
                int startIndex = (int)ItemField.ITEM_FIELD_ENCHANTMENT;
                int sizePerEntry = 3;
                if (itemData.Enchantment[i] != null)
                {
                    if (itemData.Enchantment[i]!.ID != null)
                        m_fields.SetUpdateField<int>(startIndex + i * sizePerEntry, (int)itemData.Enchantment[i]!.ID!);
                    if (itemData.Enchantment[i]!.Duration != null)
                        m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 1, (uint)itemData.Enchantment[i]!.Duration!);
                    if (itemData.Enchantment[i]!.Charges != null)
                        m_fields.SetUpdateField<ushort>(startIndex + i * sizePerEntry + 2, (ushort)itemData.Enchantment[i]!.Charges!, 0);
                    if (itemData.Enchantment[i]!.Inactive != null)
                        m_fields.SetUpdateField<ushort>(startIndex + i * sizePerEntry + 2, (ushort)itemData.Enchantment[i]!.Inactive!, 1);
                }
            }
            if (itemData.PropertySeed != null)
                m_fields.SetUpdateField<uint>(ItemField.ITEM_FIELD_PROPERTY_SEED, (uint)itemData.PropertySeed);
            if (itemData.RandomProperty != null)
                m_fields.SetUpdateField<uint>(ItemField.ITEM_FIELD_RANDOM_PROPERTIES_ID, (uint)itemData.RandomProperty);
            if (itemData.Durability != null)
                m_fields.SetUpdateField<uint>(ItemField.ITEM_FIELD_DURABILITY, (uint)itemData.Durability);
            if (itemData.MaxDurability != null)
                m_fields.SetUpdateField<uint>(ItemField.ITEM_FIELD_MAXDURABILITY, (uint)itemData.MaxDurability);
            if (itemData.CreatePlayedTime != null)
                m_fields.SetUpdateField<uint>(ItemField.ITEM_FIELD_CREATE_PLAYED_TIME, (uint)itemData.CreatePlayedTime);
            if (itemData.ModifiersMask != null)
                m_fields.SetUpdateField<uint>(ItemField.ITEM_FIELD_MODIFIERS_MASK, (uint)itemData.ModifiersMask);
            if (itemData.Context != null)
                m_fields.SetUpdateField<int>(ItemField.ITEM_FIELD_CONTEXT, (int)itemData.Context);
            if (itemData.ArtifactXP != null)
                m_fields.SetUpdateField<ulong>(ItemField.ITEM_FIELD_ARTIFACT_XP, (ulong)itemData.ArtifactXP);
            if (itemData.ItemAppearanceModID != null)
                m_fields.SetUpdateField<uint>(ItemField.ITEM_FIELD_APPEARANCE_MOD_ID, (uint)itemData.ItemAppearanceModID);

            // Dynamic Fields
            if (itemData.HasGemsUpdate)
            {
                uint[] fields = new uint[30];
                uint[] gems = m_gameState.GetGemsForItem(m_updateData.Guid)!;
                fields[0] = (uint)gems[0];
                fields[10] = (uint)gems[1];
                fields[20] = (uint)gems[2];
                m_dynamicFields.SetUpdateField((int)ItemDynamicField.ITEM_DYNAMIC_FIELD_GEMS, fields, DynamicFieldChangeType.ValueAndSizeChanged);
            }
        }

        ContainerData containerData = m_updateData.ContainerData;
        if (containerData != null)
        {
            for (int i = 0; i < 36; i++)
            {
                int startIndex = (int)ContainerField.CONTAINER_FIELD_SLOT_1;
                int sizePerEntry = 4;
                if (containerData.Slots[i] != null)
                {
                    m_fields.SetUpdateField(startIndex + i * sizePerEntry, containerData.Slots[i]!.Value);
                }
            }
            if (containerData.NumSlots != null)
                m_fields.SetUpdateField<uint>(ContainerField.CONTAINER_FIELD_NUM_SLOTS, (uint)containerData.NumSlots);
        }

        UnitData unitData = m_updateData.UnitData;
        if (unitData != null)
        {
            if (unitData.Charm != null)
                m_fields.SetUpdateField(UnitField.UNIT_FIELD_CHARM, unitData.Charm.Value);
            if (unitData.Summon != null)
                m_fields.SetUpdateField(UnitField.UNIT_FIELD_SUMMON, unitData.Summon.Value);
            if (unitData.Critter != null)
                m_fields.SetUpdateField(UnitField.UNIT_FIELD_CRITTER, unitData.Critter.Value);
            if (unitData.CharmedBy != null)
                m_fields.SetUpdateField(UnitField.UNIT_FIELD_CHARMEDBY, unitData.CharmedBy.Value);
            if (unitData.SummonedBy != null)
                m_fields.SetUpdateField(UnitField.UNIT_FIELD_SUMMONEDBY, unitData.SummonedBy.Value);
            if (unitData.CreatedBy != null)
                m_fields.SetUpdateField(UnitField.UNIT_FIELD_CREATEDBY, unitData.CreatedBy.Value);
            if (unitData.DemonCreator != null)
                m_fields.SetUpdateField(UnitField.UNIT_FIELD_DEMON_CREATOR, unitData.DemonCreator.Value);
            if (unitData.LookAtControllerTarget != null)
                m_fields.SetUpdateField(UnitField.UNIT_FIELD_LOOK_AT_CONTROLLER_TARGET, unitData.LookAtControllerTarget.Value);
            if (unitData.Target != null)
                m_fields.SetUpdateField(UnitField.UNIT_FIELD_TARGET, unitData.Target.Value);
            if (unitData.BattlePetCompanionGUID != null)
                m_fields.SetUpdateField(UnitField.UNIT_FIELD_BATTLE_PET_COMPANION_GUID, unitData.BattlePetCompanionGUID.Value);
            if (unitData.BattlePetDBID != null)
                m_fields.SetUpdateField<ulong>(UnitField.UNIT_FIELD_BATTLE_PET_DB_ID, (ulong)unitData.BattlePetDBID);
            if (unitData.ChannelData != null)
            {
                int startIndex = (int)UnitField.UNIT_FIELD_CHANNEL_DATA;
                m_fields.SetUpdateField<int>(startIndex, unitData.ChannelData.Value.SpellID);
                m_fields.SetUpdateField<int>(startIndex + 1, unitData.ChannelData.Value.SpellXSpellVisualID);
            }
            if (unitData.SummonedByHomeRealm != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_SUMMONED_BY_HOME_REALM, (uint)unitData.SummonedByHomeRealm);
            if (unitData.RaceId != null || unitData.ClassId != null || unitData.PlayerClassId != null || unitData.SexId != null)
            {
                if (unitData.RaceId != null)
                    m_fields.SetUpdateField<byte>(UnitField.UNIT_FIELD_BYTES_0, (byte)unitData.RaceId, 0);
                if (unitData.ClassId != null)
                    m_fields.SetUpdateField<byte>(UnitField.UNIT_FIELD_BYTES_0, (byte)unitData.ClassId, 1);
                if (unitData.PlayerClassId != null)
                    m_fields.SetUpdateField<byte>(UnitField.UNIT_FIELD_BYTES_0, (byte)unitData.PlayerClassId, 2);
                if (unitData.SexId != null)
                    m_fields.SetUpdateField<byte>(UnitField.UNIT_FIELD_BYTES_0, (byte)unitData.SexId, 3);
            }
            if (unitData.DisplayPower != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_DISPLAY_POWER, (uint)unitData.DisplayPower);
            if (unitData.OverrideDisplayPowerID != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_OVERRIDE_DISPLAY_POWER_ID, (uint)unitData.OverrideDisplayPowerID);
            if (unitData.Health != null)
                m_fields.SetUpdateField<ulong>(UnitField.UNIT_FIELD_HEALTH, (ulong)unitData.Health);
            for (int i = 0; i < 7; i++)
            {
                int startIndex = (int)UnitField.UNIT_FIELD_POWER;
                if (unitData.Power[i] != null)
                    m_fields.SetUpdateField<int>(startIndex + i, (int)unitData.Power[i]!);
            }
            if (unitData.MaxHealth != null)
                m_fields.SetUpdateField<ulong>(UnitField.UNIT_FIELD_MAXHEALTH, (ulong)unitData.MaxHealth);
            for (int i = 0; i < 7; i++)
            {
                int startIndex = (int)UnitField.UNIT_FIELD_MAXPOWER;
                if (unitData.MaxPower[i] != null)
                    m_fields.SetUpdateField<int>(startIndex + i, (int)unitData.MaxPower[i]!);
            }
            for (int i = 0; i < 7; i++)
            {
                int startIndex = (int)UnitField.UNIT_FIELD_MOD_POWER_REGEN;
                if (unitData.ModPowerRegen[i] != null)
                    m_fields.SetUpdateField<float>(startIndex + i, (float)unitData.ModPowerRegen[i]!);
            }
            if (unitData.Level != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_LEVEL, (int)unitData.Level);
            if (unitData.EffectiveLevel != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_EFFECTIVE_LEVEL, (int)unitData.EffectiveLevel);
            if (unitData.ContentTuningID != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_CONTENT_TUNING_ID, (int)unitData.ContentTuningID);
            if (unitData.ScalingLevelMin != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_SCALING_LEVEL_MIN, (int)unitData.ScalingLevelMin);
            if (unitData.ScalingLevelMax != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_SCALING_LEVEL_MAX, (int)unitData.ScalingLevelMax);
            if (unitData.ScalingLevelDelta != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_SCALING_LEVEL_DELTA, (int)unitData.ScalingLevelDelta);
            if (unitData.ScalingFactionGroup != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_SCALING_FACTION_GROUP, (int)unitData.ScalingFactionGroup);
            if (unitData.ScalingHealthItemLevelCurveID != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_SCALING_HEALTH_ITEM_LEVEL_CURVE_ID, (int)unitData.ScalingHealthItemLevelCurveID);
            if (unitData.ScalingDamageItemLevelCurveID != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_SCALING_DAMAGE_ITEM_LEVEL_CURVE_ID, (int)unitData.ScalingDamageItemLevelCurveID);
            if (unitData.FactionTemplate != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_FACTIONTEMPLATE, (int)unitData.FactionTemplate);
            for (int i = 0; i < 3; i++)
            {
                int startIndex = (int)UnitField.UNIT_VIRTUAL_ITEM_SLOT_ID;
                int sizePerEntry = 2;
                if (unitData.VirtualItems[i] != null)
                {
                    m_fields.SetUpdateField<int>(startIndex + i * sizePerEntry, unitData.VirtualItems[i]!.Value.ItemID);
                    m_fields.SetUpdateField<ushort>(startIndex + i * sizePerEntry + 1, unitData.VirtualItems[i]!.Value.ItemAppearanceModID, 0);
                    m_fields.SetUpdateField<ushort>(startIndex + i * sizePerEntry + 1, unitData.VirtualItems[i]!.Value.ItemVisual, 1);
                }
            }
            if (unitData.Flags != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_FLAGS, (uint)unitData.Flags);
            if (unitData.Flags2 != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_FLAGS_2, (uint)unitData.Flags2);
            if (unitData.Flags3 != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_FLAGS_3, (uint)unitData.Flags3);
            if (unitData.AuraState != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_AURASTATE, (uint)unitData.AuraState);
            for (int i = 0; i < 2; i++)
            {
                int startIndex = (int)UnitField.UNIT_FIELD_BASEATTACKTIME;
                if (unitData.AttackRoundBaseTime[i] != null)
                    m_fields.SetUpdateField<uint>(startIndex + i, (uint)unitData.AttackRoundBaseTime[i]!);
            }
            if (unitData.RangedAttackRoundBaseTime != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_RANGEDATTACKTIME, (uint)unitData.RangedAttackRoundBaseTime);
            if (unitData.BoundingRadius != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_BOUNDINGRADIUS, (float)unitData.BoundingRadius);
            if (unitData.CombatReach != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_COMBATREACH, (float)unitData.CombatReach);
            if (unitData.DisplayID != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_DISPLAYID, (int)unitData.DisplayID);
            if (unitData.DisplayScale != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_DISPLAY_SCALE, (float)unitData.DisplayScale);
            if (unitData.NativeDisplayID != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_NATIVEDISPLAYID, (int)unitData.NativeDisplayID);
            if (unitData.NativeXDisplayScale != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_NATIVE_X_DISPLAY_SCALE, (float)unitData.NativeXDisplayScale);
            if (unitData.MountDisplayID != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_MOUNTDISPLAYID, (int)unitData.MountDisplayID);
            if (unitData.MinDamage != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_MINDAMAGE, (float)unitData.MinDamage);
            if (unitData.MaxDamage != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_MAXDAMAGE, (float)unitData.MaxDamage);
            if (unitData.MinOffHandDamage != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_MINOFFHANDDAMAGE, (float)unitData.MinOffHandDamage);
            if (unitData.MaxOffHandDamage != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_MAXOFFHANDDAMAGE, (float)unitData.MaxOffHandDamage);
            if (unitData.StandState != null || unitData.PetLoyaltyIndex != null || unitData.VisFlags != null || unitData.AnimTier != null)
            {
                if (unitData.StandState != null)
                    m_fields.SetUpdateField<byte>(UnitField.UNIT_FIELD_BYTES_1, (byte)unitData.StandState, 0);
                if (unitData.PetLoyaltyIndex != null)
                    m_fields.SetUpdateField<byte>(UnitField.UNIT_FIELD_BYTES_1, (byte)unitData.PetLoyaltyIndex, 1);
                if (unitData.VisFlags != null)
                    m_fields.SetUpdateField<byte>(UnitField.UNIT_FIELD_BYTES_1, (byte)unitData.VisFlags, 2);
                if (unitData.AnimTier != null)
                    m_fields.SetUpdateField<byte>(UnitField.UNIT_FIELD_BYTES_1, (byte)unitData.AnimTier, 3);
            }
            if (unitData.PetNumber != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_PETNUMBER, (uint)unitData.PetNumber);
            if (unitData.PetNameTimestamp != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_PET_NAME_TIMESTAMP, (uint)unitData.PetNameTimestamp);
            if (unitData.PetExperience != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_PETEXPERIENCE, (uint)unitData.PetExperience);
            if (unitData.PetNextLevelExperience != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_PETNEXTLEVELXP, (uint)unitData.PetNextLevelExperience);
            if (unitData.ModCastSpeed != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_MOD_CAST_SPEED, (float)unitData.ModCastSpeed);
            if (unitData.ModCastHaste != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_MOD_CAST_HASTE, (float)unitData.ModCastHaste);
            if (unitData.ModHaste != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_MOD_HASTE, (float)unitData.ModHaste);
            if (unitData.ModRangedHaste != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_MOD_RANGED_HASTE, (float)unitData.ModRangedHaste);
            if (unitData.ModHasteRegen != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_MOD_HASTE_REGEN, (float)unitData.ModHasteRegen);
            if (unitData.ModTimeRate != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_MOD_TIME_RATE, (float)unitData.ModTimeRate);
            if (unitData.CreatedBySpell != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_CREATED_BY_SPELL, (int)unitData.CreatedBySpell);
            for (int i = 0; i < 2; i++)
            {
                int startIndex = (int)UnitField.UNIT_NPC_FLAGS;
                if (unitData.NpcFlags[i] != null)
                    m_fields.SetUpdateField<uint>(startIndex + i, (uint)unitData.NpcFlags[i]!);
            }
            if (unitData.EmoteState != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_NPC_EMOTESTATE, (int)unitData.EmoteState);
            if (unitData.TrainingPointsUsed != null && unitData.TrainingPointsTotal != null)
            {
                m_fields.SetUpdateField<ushort>(UnitField.UNIT_FIELD_TRAINING_POINTS_TOTAL, (ushort)unitData.TrainingPointsUsed, 0);
                m_fields.SetUpdateField<ushort>(UnitField.UNIT_FIELD_TRAINING_POINTS_TOTAL, (ushort)unitData.TrainingPointsTotal, 1);
            }
            for (int i = 0; i < 5; i++)
            {
                int startIndex = (int)UnitField.UNIT_FIELD_STAT;
                if (unitData.Stats[i] != null)
                    m_fields.SetUpdateField<int>(startIndex + i, (int)unitData.Stats[i]!);
            }
            for (int i = 0; i < 5; i++)
            {
                int startIndex = (int)UnitField.UNIT_FIELD_POSSTAT;
                if (unitData.StatPosBuff[i] != null)
                    m_fields.SetUpdateField<int>(startIndex + i, (int)unitData.StatPosBuff[i]!);
            }
            for (int i = 0; i < 5; i++)
            {
                int startIndex = (int)UnitField.UNIT_FIELD_NEGSTAT;
                if (unitData.StatNegBuff[i] != null)
                    m_fields.SetUpdateField<int>(startIndex + i, (int)unitData.StatNegBuff[i]!);
            }
            for (int i = 0; i < 7; i++)
            {
                int startIndex = (int)UnitField.UNIT_FIELD_RESISTANCES;
                if (unitData.Resistances[i] != null)
                    m_fields.SetUpdateField<int>(startIndex + i, (int)unitData.Resistances[i]!);
            }
            for (int i = 0; i < 7; i++)
            {
                int startIndex = (int)UnitField.UNIT_FIELD_RESISTANCEBUFFMODSPOSITIVE;
                if (unitData.ResistanceBuffModsPositive[i] != null)
                    m_fields.SetUpdateField<int>(startIndex + i, (int)unitData.ResistanceBuffModsPositive[i]!);
            }
            for (int i = 0; i < 7; i++)
            {
                int startIndex = (int)UnitField.UNIT_FIELD_RESISTANCEBUFFMODSNEGATIVE;
                if (unitData.ResistanceBuffModsNegative[i] != null)
                    m_fields.SetUpdateField<int>(startIndex + i, (int)unitData.ResistanceBuffModsNegative[i]!);
            }
            if (unitData.BaseMana != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_BASE_MANA, (int)unitData.BaseMana);
            if (unitData.BaseHealth != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_BASE_HEALTH, (int)unitData.BaseHealth);
            if (unitData.SheatheState != null || unitData.PvpFlags != null || unitData.PetFlags != null || unitData.ShapeshiftForm != null)
            {
                if (unitData.SheatheState != null)
                    m_fields.SetUpdateField<byte>(UnitField.UNIT_FIELD_BYTES_2, (byte)unitData.SheatheState, 0);
                if (unitData.PvpFlags != null)
                    m_fields.SetUpdateField<byte>(UnitField.UNIT_FIELD_BYTES_2, (byte)unitData.PvpFlags, 1);
                if (unitData.PetFlags != null)
                    m_fields.SetUpdateField<byte>(UnitField.UNIT_FIELD_BYTES_2, (byte)unitData.PetFlags, 2);
                if (unitData.ShapeshiftForm != null)
                    m_fields.SetUpdateField<byte>(UnitField.UNIT_FIELD_BYTES_2, (byte)unitData.ShapeshiftForm, 3);
            }
            if (unitData.AttackPower != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_ATTACK_POWER, (int)unitData.AttackPower);
            if (unitData.AttackPowerModPos != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_ATTACK_POWER_MOD_POS, (int)unitData.AttackPowerModPos);
            if (unitData.AttackPowerModNeg != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_ATTACK_POWER_MOD_NEG, (int)unitData.AttackPowerModNeg);
            if (unitData.AttackPowerMultiplier != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_ATTACK_POWER_MULTIPLIER, (float)unitData.AttackPowerMultiplier);
            if (unitData.RangedAttackPower != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_RANGED_ATTACK_POWER, (int)unitData.RangedAttackPower);
            if (unitData.RangedAttackPowerModPos != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_RANGED_ATTACK_POWER_MOD_POS, (int)unitData.RangedAttackPowerModPos);
            if (unitData.RangedAttackPowerModNeg != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_RANGED_ATTACK_POWER_MOD_NEG, (int)unitData.RangedAttackPowerModNeg);
            if (unitData.RangedAttackPowerMultiplier != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_RANGED_ATTACK_POWER_MULTIPLIER, (float)unitData.RangedAttackPowerMultiplier);
            if (unitData.AttackSpeedAura != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_ATTACK_SPEED_AURA, (int)unitData.AttackSpeedAura);
            if (unitData.Lifesteal != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_LIFESTEAL, (float)unitData.Lifesteal);
            if (unitData.MinRangedDamage != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_MINRANGEDDAMAGE, (float)unitData.MinRangedDamage);
            if (unitData.MaxRangedDamage != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_MAXRANGEDDAMAGE, (float)unitData.MaxRangedDamage);
            for (int i = 0; i < 7; i++)
            {
                int startIndex = (int)UnitField.UNIT_FIELD_POWER_COST_MODIFIER;
                if (unitData.PowerCostModifier[i] != null)
                    m_fields.SetUpdateField<int>(startIndex + i, (int)unitData.PowerCostModifier[i]!);
            }
            for (int i = 0; i < 7; i++)
            {
                int startIndex = (int)UnitField.UNIT_FIELD_POWER_COST_MULTIPLIER;
                if (unitData.PowerCostMultiplier[i] != null)
                    m_fields.SetUpdateField<float>(startIndex + i, (float)unitData.PowerCostMultiplier[i]!);
            }
            if (unitData.MaxHealthModifier != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_MAXHEALTHMODIFIER, (float)unitData.MaxHealthModifier);
            if (unitData.HoverHeight != null)
                m_fields.SetUpdateField<float>(UnitField.UNIT_FIELD_HOVERHEIGHT, (float)unitData.HoverHeight);
            if (unitData.MinItemLevelCutoff != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_MIN_ITEM_LEVEL_CUTOFF, (int)unitData.MinItemLevelCutoff);
            if (unitData.MinItemLevel != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_MIN_ITEM_LEVEL, (int)unitData.MinItemLevel);
            if (unitData.MaxItemLevel != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_MAXITEMLEVEL, (int)unitData.MaxItemLevel);
            if (unitData.WildBattlePetLevel != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_WILD_BATTLEPET_LEVEL, (int)unitData.WildBattlePetLevel);
            if (unitData.BattlePetCompanionNameTimestamp != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_BATTLEPET_COMPANION_NAME_TIMESTAMP, (uint)unitData.BattlePetCompanionNameTimestamp);
            if (unitData.InteractSpellID != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_INTERACT_SPELLID, (int)unitData.InteractSpellID);
            if (unitData.StateSpellVisualID != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_STATE_SPELL_VISUAL_ID, (uint)unitData.StateSpellVisualID);
            if (unitData.StateAnimID != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_STATE_ANIM_ID, (uint)unitData.StateAnimID);
            if (unitData.StateAnimKitID != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_STATE_ANIM_KIT_ID, (uint)unitData.StateAnimKitID);
            if (unitData.StateWorldEffectsID != null)
                m_fields.SetUpdateField<uint>(UnitField.UNIT_FIELD_STATE_WORLD_EFFECT_ID, (uint)unitData.StateWorldEffectsID);
            if (unitData.ScaleDuration != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_SCALE_DURATION, (int)unitData.ScaleDuration);
            if (unitData.LooksLikeMountID != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_LOOKS_LIKE_MOUNT_ID, (int)unitData.LooksLikeMountID);
            if (unitData.LooksLikeCreatureID != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_LOOKS_LIKE_CREATURE_ID, (int)unitData.LooksLikeCreatureID);
            if (unitData.LookAtControllerID != null)
                m_fields.SetUpdateField<int>(UnitField.UNIT_FIELD_LOOK_AT_CONTROLLER_ID, (int)unitData.LookAtControllerID);
            if (unitData.GuildGUID != null)
                m_fields.SetUpdateField(UnitField.UNIT_FIELD_GUILD_GUID, unitData.GuildGUID.Value);

            // Dynamic Fields
            if (unitData.ChannelObject != null)
                m_dynamicFields.SetUpdateField<WowGuid128>(UnitDynamicField.UNIT_DYNAMIC_FIELD_CHANNEL_OBJECTS, unitData.ChannelObject.Value, DynamicFieldChangeType.ValueAndSizeChanged);
        }

        PlayerData playerData = m_updateData.PlayerData;
        if (playerData != null)
        {
            if (playerData.DuelArbiter != null)
                m_fields.SetUpdateField(PlayerField.PLAYER_DUEL_ARBITER, playerData.DuelArbiter.Value);
            if (playerData.WowAccount != null)
                m_fields.SetUpdateField(PlayerField.PLAYER_WOW_ACCOUNT, playerData.WowAccount.Value);
            if (playerData.LootTargetGUID != null)
                m_fields.SetUpdateField(PlayerField.PLAYER_LOOT_TARGET_GUID, playerData.LootTargetGUID.Value);
            if (playerData.PlayerFlags != null)
                m_fields.SetUpdateField<uint>(PlayerField.PLAYER_FLAGS, (uint)playerData.PlayerFlags);
            if (playerData.PlayerFlagsEx != null)
                m_fields.SetUpdateField<uint>(PlayerField.PLAYER_FLAGS_EX, (uint)playerData.PlayerFlagsEx);
            if (playerData.GuildRankID != null)
                m_fields.SetUpdateField<uint>(PlayerField.PLAYER_GUILDRANK, (uint)playerData.GuildRankID);
            if (playerData.GuildDeleteDate != null)
                m_fields.SetUpdateField<uint>(PlayerField.PLAYER_GUILDDELETE_DATE, (uint)playerData.GuildDeleteDate);
            if (playerData.GuildLevel != null)
                m_fields.SetUpdateField<int>(PlayerField.PLAYER_GUILDLEVEL, (int)playerData.GuildLevel);
            if (playerData.PartyType != null || playerData.NumBankSlots != null || playerData.NativeSex != null || playerData.Inebriation != null)
            {
                if (playerData.PartyType != null)
                    m_fields.SetUpdateField<byte>(PlayerField.PLAYER_BYTES, (byte)playerData.PartyType, 0);
                if (playerData.NumBankSlots != null)
                    m_fields.SetUpdateField<byte>(PlayerField.PLAYER_BYTES, (byte)playerData.NumBankSlots, 1);
                if (playerData.NativeSex != null)
                    m_fields.SetUpdateField<byte>(PlayerField.PLAYER_BYTES, (byte)playerData.NativeSex, 2);
                if (playerData.Inebriation != null)
                    m_fields.SetUpdateField<byte>(PlayerField.PLAYER_BYTES, (byte)playerData.Inebriation, 3);
            }
            if (playerData.PvpTitle != null || playerData.ArenaFaction != null || playerData.PvPRank != null)
            {
                if (playerData.PvpTitle != null)
                    m_fields.SetUpdateField<byte>(PlayerField.PLAYER_BYTES_2, (byte)playerData.PvpTitle, 0);
                if (playerData.ArenaFaction != null)
                    m_fields.SetUpdateField<byte>(PlayerField.PLAYER_BYTES_2, (byte)playerData.ArenaFaction, 1);
                if (playerData.PvPRank != null)
                    m_fields.SetUpdateField<byte>(PlayerField.PLAYER_BYTES_2, (byte)playerData.PvPRank, 2);
            }
            if (playerData.DuelTeam != null)
                m_fields.SetUpdateField<uint>(PlayerField.PLAYER_DUEL_TEAM, (uint)playerData.DuelTeam);
            if (playerData.GuildTimeStamp != null)
                m_fields.SetUpdateField<int>(PlayerField.PLAYER_GUILD_TIMESTAMP, (int)playerData.GuildTimeStamp);
            for (int i = 0; i < 25; i++)
            {
                int startIndex = (int)PlayerField.PLAYER_QUEST_LOG;
                int sizePerEntry = 16;
                if (playerData.QuestLog[i] != null)
                {
                    if (playerData.QuestLog[i].QuestID != null)
                        m_fields.SetUpdateField<int>(startIndex + i * sizePerEntry, (int)playerData.QuestLog[i].QuestID!);
                    if (playerData.QuestLog[i].StateFlags != null)
                        m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 1, (uint)playerData.QuestLog[i].StateFlags!);
                    for (int j = 0; j < 24; j++)
                    {
                        if (playerData.QuestLog[i].ObjectiveProgress[j] != null)
                            m_fields.SetUpdateField<ushort>(startIndex + i * sizePerEntry + 2 + j / 2, (ushort)playerData.QuestLog[i].ObjectiveProgress[j]!, (byte)(j & 1));
                    }
                    if (playerData.QuestLog[i].EndTime != null)
                        m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 2 + 12, (uint)playerData.QuestLog[i].EndTime!);
                    if (playerData.QuestLog[i].AcceptTime != null)
                        m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 3 + 12, (uint)playerData.QuestLog[i].AcceptTime!); 
                }
            }
            for (int i = 0; i < 19; i++)
            {
                int startIndex = (int)PlayerField.PLAYER_VISIBLE_ITEM;
                int sizePerEntry = 2;
                if (playerData.VisibleItems[i] != null)
                {
                    m_fields.SetUpdateField<int>(startIndex + i * sizePerEntry, playerData.VisibleItems[i]!.Value.ItemID);
                    m_fields.SetUpdateField<ushort>(startIndex + i * sizePerEntry + 1, playerData.VisibleItems[i]!.Value.ItemAppearanceModID, 0);
                    m_fields.SetUpdateField<ushort>(startIndex + i * sizePerEntry + 1, playerData.VisibleItems[i]!.Value.ItemVisual, 1);
                }
            }
            if (playerData.ChosenTitle != null)
                m_fields.SetUpdateField<int>(PlayerField.PLAYER_CHOSEN_TITLE, (int)playerData.ChosenTitle);
            if (playerData.FakeInebriation != null)
                m_fields.SetUpdateField<int>(PlayerField.PLAYER_FAKE_INEBRIATION, (int)playerData.FakeInebriation);
            if (playerData.VirtualPlayerRealm != null)
                m_fields.SetUpdateField<uint>(PlayerField.PLAYER_FIELD_VIRTUAL_PLAYER_REALM, (uint)playerData.VirtualPlayerRealm);
            if (playerData.CurrentSpecID != null)
                m_fields.SetUpdateField<uint>(PlayerField.PLAYER_FIELD_CURRENT_SPEC_ID, (uint)playerData.CurrentSpecID);
            if (playerData.TaxiMountAnimKitID != null)
                m_fields.SetUpdateField<int>(PlayerField.PLAYER_FIELD_TAXI_MOUNT_ANIM_KIT_ID, (int)playerData.TaxiMountAnimKitID);
            for (int i = 0; i < 6; i++)
            {
                int startIndex = (int)PlayerField.PLAYER_FIELD_AVG_ITEM_LEVEL;
                if (playerData.AvgItemLevel[i] != null)
                    m_fields.SetUpdateField<float>(startIndex + i, (float)playerData.AvgItemLevel[i]!);
            }
            if (playerData.CurrentBattlePetBreedQuality != null)
                m_fields.SetUpdateField<uint>(PlayerField.PLAYER_FIELD_CURRENT_BATTLE_PET_BREED_QUALITY, (uint)playerData.CurrentBattlePetBreedQuality);
            if (playerData.HonorLevel != null)
                m_fields.SetUpdateField<int>(PlayerField.PLAYER_FIELD_HONOR_LEVEL, (int)playerData.HonorLevel);
            for (int i = 0; i < 35; i++)
            {
                int startIndex = (int)PlayerField.PLAYER_FIELD_CUSTOMIZATION_CHOICES;
                int sizePerEntry = 2;
                if (playerData.Customizations[i] != null)
                {
                    m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry, (uint)playerData.Customizations[i].ChrCustomizationOptionID);
                    m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 1, (uint)playerData.Customizations[i].ChrCustomizationChoiceID);
                }
            }
        }

        ActivePlayerData activeData = m_updateData.ActivePlayerData;
        if (activeData != null && m_objectType == Enums.ObjectTypeBCC.ActivePlayer)
        {
            for (int i = 0; i < 23; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_INV_SLOT_HEAD;
                int sizePerEntry = 4;
                if (activeData.InvSlots[i] != null)
                    m_fields.SetUpdateField(startIndex + i * sizePerEntry, activeData.InvSlots[i]!.Value);
            }
            for (int i = 0; i < 24; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_INV_SLOT_HEAD + Enums.Classic.InventorySlots.ItemStart * 4;
                int sizePerEntry = 4;
                if (activeData.PackSlots[i] != null)
                    m_fields.SetUpdateField(startIndex + i * sizePerEntry, activeData.PackSlots[i]!.Value);
            }
            for (int i = 0; i < 28; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_INV_SLOT_HEAD + Enums.Classic.InventorySlots.BankItemStart * 4;
                int sizePerEntry = 4;
                if (activeData.BankSlots[i] != null)
                    m_fields.SetUpdateField(startIndex + i * sizePerEntry, activeData.BankSlots[i]!.Value);
            }
            for (int i = 0; i < 7; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_INV_SLOT_HEAD + Enums.Classic.InventorySlots.BankBagStart * 4;
                int sizePerEntry = 4;
                if (activeData.BankBagSlots[i] != null)
                    m_fields.SetUpdateField(startIndex + i * sizePerEntry, activeData.BankBagSlots[i]!.Value);
            }
            for (int i = 0; i < 12; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_INV_SLOT_HEAD + Enums.Classic.InventorySlots.BuyBackStart * 4;
                int sizePerEntry = 4;
                if (activeData.BuyBackSlots[i] != null)
                    m_fields.SetUpdateField(startIndex + i * sizePerEntry, activeData.BuyBackSlots[i]!.Value);
            }
            for (int i = 0; i < 32; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_INV_SLOT_HEAD + Enums.Classic.InventorySlots.KeyringStart * 4;
                int sizePerEntry = 4;
                if (activeData.KeyringSlots[i] != null)
                    m_fields.SetUpdateField(startIndex + i * sizePerEntry, activeData.KeyringSlots[i]!.Value);
            }
            if (activeData.FarsightObject != null)
                m_fields.SetUpdateField(ActivePlayerField.ACTIVE_PLAYER_FIELD_FARSIGHT, activeData.FarsightObject.Value);
            if (activeData.ComboTarget != null)
                m_fields.SetUpdateField(ActivePlayerField.ACTIVE_PLAYER_FIELD_COMBO_TARGET, activeData.ComboTarget.Value);
            if (activeData.SummonedBattlePetGUID != null)
                m_fields.SetUpdateField(ActivePlayerField.ACTIVE_PLAYER_FIELD_SUMMONED_BATTLE_PET_ID, activeData.SummonedBattlePetGUID.Value);
            for (int i = 0; i < 12; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_KNOWN_TITLES;
                if (activeData.KnownTitles[i] != null)
                    m_fields.SetUpdateField<uint>(startIndex + i, (uint)activeData.KnownTitles[i]!);
            }
            if (activeData.Coinage != null)
                m_fields.SetUpdateField<ulong>(ActivePlayerField.ACTIVE_PLAYER_FIELD_COINAGE, (ulong)activeData.Coinage);
            if (activeData.XP != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_XP, (int)activeData.XP);
            if (activeData.NextLevelXP != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_NEXT_LEVEL_XP, (int)activeData.NextLevelXP);
            if (activeData.TrialXP != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_TRIAL_XP, (int)activeData.TrialXP);
            for (int i = 0; i < 256; i++)
            {
                if (activeData.Skill.SkillLineID[i] != null)
                {
                    int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_SKILL_LINEID;
                    m_fields.SetUpdateField<ushort>(startIndex + i / 2, (ushort)activeData.Skill.SkillLineID[i]!, (byte)(i & 1));
                }
                if (activeData.Skill.SkillStep[i] != null)
                {
                    int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_SKILL_LINEID + 128;
                    m_fields.SetUpdateField<ushort>(startIndex + i / 2, (ushort)activeData.Skill.SkillStep[i]!, (byte)(i & 1));
                }
                if (activeData.Skill.SkillRank[i] != null)
                {
                    int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_SKILL_LINEID + 128 + 128;
                    m_fields.SetUpdateField<ushort>(startIndex + i / 2, (ushort)activeData.Skill.SkillRank[i]!, (byte)(i & 1));
                }
                if (activeData.Skill.SkillStartingRank[i] != null)
                {
                    int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_SKILL_LINEID + 128 + 128 + 128;
                    m_fields.SetUpdateField<ushort>(startIndex + i / 2, (ushort)activeData.Skill.SkillStartingRank[i]!, (byte)(i & 1));
                }
                if (activeData.Skill.SkillMaxRank[i] != null)
                {
                    int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_SKILL_LINEID + 128 + 128 + 128 + 128;
                    m_fields.SetUpdateField<ushort>(startIndex + i / 2, (ushort)activeData.Skill.SkillMaxRank[i]!, (byte)(i & 1));
                }
                if (activeData.Skill.SkillTempBonus[i] != null)
                {
                    int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_SKILL_LINEID + 128 + 128 + 128 + 128 + 128;
                    m_fields.SetUpdateField<ushort>(startIndex + i / 2, (ushort)activeData.Skill.SkillTempBonus[i]!, (byte)(i & 1));
                }
                if (activeData.Skill.SkillPermBonus[i] != null)
                {
                    int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_SKILL_LINEID + 128 + 128 + 128 + 128 + 128 + 128;
                    m_fields.SetUpdateField<ushort>(startIndex + i / 2, (ushort)activeData.Skill.SkillPermBonus[i]!, (byte)(i & 1));
                }
            }
            if (activeData.CharacterPoints != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_CHARACTER_POINTS, (int)activeData.CharacterPoints);
            if (activeData.MaxTalentTiers != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_MAX_TALENT_TIERS, (int)activeData.MaxTalentTiers);
            if (activeData.TrackCreatureMask != null)
                m_fields.SetUpdateField<uint>(ActivePlayerField.ACTIVE_PLAYER_FIELD_TRACK_CREATURES, (uint)activeData.TrackCreatureMask);
            for (int i = 0; i < 2; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_TRACK_RESOURCES;
                if (activeData.TrackResourceMask[i] != null)
                    m_fields.SetUpdateField<uint>(startIndex + i, (uint)activeData.TrackResourceMask[i]!);
            }
            if (activeData.MainhandExpertise != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_EXPERTISE, (float)activeData.MainhandExpertise);
            if (activeData.OffhandExpertise != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_OFFHAND_EXPERTISE, (float)activeData.OffhandExpertise);
            if (activeData.RangedExpertise != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_RANGED_EXPERTISE, (float)activeData.RangedExpertise);
            if (activeData.CombatRatingExpertise != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_COMBAT_RATING_EXPERTISE, (float)activeData.CombatRatingExpertise);
            if (activeData.BlockPercentage != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BLOCK_PERCENTAGE, (float)activeData.BlockPercentage);
            if (activeData.DodgePercentage != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_DODGE_PERCENTAGE, (float)activeData.DodgePercentage);
            if (activeData.DodgePercentageFromAttribute != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_DODGE_PERCENTAGE_FROM_ATTRIBUTE, (float)activeData.DodgePercentageFromAttribute);
            if (activeData.ParryPercentage != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_PARRY_PERCENTAGE, (float)activeData.ParryPercentage);
            if (activeData.ParryPercentageFromAttribute != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_PARRY_PERCENTAGE_FROM_ATTRIBUTE, (float)activeData.ParryPercentageFromAttribute);
            if (activeData.CritPercentage != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_CRIT_PERCENTAGE, (float)activeData.CritPercentage);
            if (activeData.RangedCritPercentage != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_RANGED_CRIT_PERCENTAGE, (float)activeData.RangedCritPercentage);
            if (activeData.OffhandCritPercentage != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_OFFHAND_CRIT_PERCENTAGE, (float)activeData.OffhandCritPercentage);
            for (int i = 0; i < 7; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_SPELL_CRIT_PERCENTAGE1;
                if (activeData.SpellCritPercentage[i] != null)
                    m_fields.SetUpdateField<float>(startIndex + i, (float)activeData.SpellCritPercentage[i]!);
            }
            if (activeData.ShieldBlock != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_SHIELD_BLOCK, (int)activeData.ShieldBlock);
            if (activeData.Mastery != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_MASTERY, (float)activeData.Mastery);
            if (activeData.Speed != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_SPEED, (float)activeData.Speed);
            if (activeData.Avoidance != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_AVOIDANCE, (float)activeData.Avoidance);
            if (activeData.Sturdiness != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_STURDINESS, (float)activeData.Sturdiness);
            if (activeData.Versatility != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_VERSATILITY, (int)activeData.Versatility);
            if (activeData.VersatilityBonus != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_VERSATILITY_BONUS, (float)activeData.VersatilityBonus);
            if (activeData.PvpPowerDamage != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_PVP_POWER_DAMAGE, (float)activeData.PvpPowerDamage);
            if (activeData.PvpPowerHealing != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_PVP_POWER_HEALING, (float)activeData.PvpPowerHealing);
            for (int i = 0; i < 240; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_EXPLORED_ZONES;
                if (activeData.ExploredZones[i] != null)
                    m_fields.SetUpdateField<ulong>(startIndex + i * 2, (ulong)activeData.ExploredZones[i]!);
            }
            for (int i = 0; i < 2; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_REST_INFO;
                int sizePerEntry = 2;
                if (activeData.RestInfo[i] != null)
                {
                    if (activeData.RestInfo[i].StateID != null)
                        m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry, (uint)activeData.RestInfo[i].StateID!);
                    if (activeData.RestInfo[i].Threshold != null)
                        m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 1, (uint)activeData.RestInfo[i].Threshold!);
                }
            }
            for (int i = 0; i < 7; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_MOD_DAMAGE_DONE_POS;
                if (activeData.ModDamageDonePos[i] != null)
                    m_fields.SetUpdateField<int>(startIndex + i, (int)activeData.ModDamageDonePos[i]!);
            }
            for (int i = 0; i < 7; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_MOD_DAMAGE_DONE_NEG;
                if (activeData.ModDamageDoneNeg[i] != null)
                    m_fields.SetUpdateField<int>(startIndex + i, (int)activeData.ModDamageDoneNeg[i]!);
            }
            for (int i = 0; i < 7; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_MOD_DAMAGE_DONE_PCT;
                if (activeData.ModDamageDonePercent[i] != null)
                    m_fields.SetUpdateField<float>(startIndex + i, (float)activeData.ModDamageDonePercent[i]!);
            }
            if (activeData.ModHealingDonePos != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_MOD_HEALING_DONE_POS, (int)activeData.ModHealingDonePos);
            if (activeData.ModHealingPercent != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_MOD_HEALING_PCT, (float)activeData.ModHealingPercent);
            if (activeData.ModHealingDonePercent != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_MOD_HEALING_DONE_PCT, (float)activeData.ModHealingDonePercent);
            if (activeData.ModPeriodicHealingDonePercent != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_MOD_PERIODIC_HEALING_DONE_PERCENT, (float)activeData.ModPeriodicHealingDonePercent);
            for (int i = 0; i < 3; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_WEAPON_DMG_MULTIPLIERS;
                if (activeData.WeaponDmgMultipliers[i] != null)
                    m_fields.SetUpdateField<float>(startIndex + i, (float)activeData.WeaponDmgMultipliers[i]!);
            }
            for (int i = 0; i < 3; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_WEAPON_ATK_SPEED_MULTIPLIERS;
                if (activeData.WeaponAtkSpeedMultipliers[i] != null)
                    m_fields.SetUpdateField<float>(startIndex + i, (float)activeData.WeaponAtkSpeedMultipliers[i]!);
            }
            if (activeData.ModSpellPowerPercent != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_MOD_SPELL_POWER_PCT, (float)activeData.ModSpellPowerPercent);
            if (activeData.ModResiliencePercent != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_MOD_RESILIENCE_PERCENT, (float)activeData.ModResiliencePercent);
            if (activeData.OverrideSpellPowerByAPPercent != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_OVERRIDE_SPELL_POWER_BY_AP_PCT, (float)activeData.OverrideSpellPowerByAPPercent);
            if (activeData.OverrideAPBySpellPowerPercent != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_OVERRIDE_AP_BY_SPELL_POWER_PERCENT, (float)activeData.OverrideAPBySpellPowerPercent);
            if (activeData.ModTargetResistance != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_MOD_TARGET_RESISTANCE, (int)activeData.ModTargetResistance);
            if (activeData.ModTargetPhysicalResistance != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_MOD_TARGET_PHYSICAL_RESISTANCE, (int)activeData.ModTargetPhysicalResistance);
            if (activeData.LocalFlags != null)
                m_fields.SetUpdateField<uint>(ActivePlayerField.ACTIVE_PLAYER_FIELD_LOCAL_FLAGS, (uint)activeData.LocalFlags);
            if (activeData.GrantableLevels != null || activeData.MultiActionBars != null || activeData.LifetimeMaxRank != null || activeData.NumRespecs != null)
            {
                if (activeData.GrantableLevels != null)
                    m_fields.SetUpdateField<byte>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES, (byte)activeData.GrantableLevels, 0);
                if (activeData.MultiActionBars != null)
                    m_fields.SetUpdateField<byte>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES, (byte)activeData.MultiActionBars, 1);
                if (activeData.LifetimeMaxRank != null)
                    m_fields.SetUpdateField<byte>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES, (byte)activeData.LifetimeMaxRank, 2);
                if (activeData.NumRespecs != null)
                    m_fields.SetUpdateField<byte>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES, (byte)activeData.NumRespecs, 3);
            }
            if (activeData.AmmoID != null)
                m_fields.SetUpdateField<uint>(ActivePlayerField.ACTIVE_PLAYER_FIELD_AMMO_ID, (uint)activeData.AmmoID);
            if (activeData.PvpMedals != null)
                m_fields.SetUpdateField<uint>(ActivePlayerField.ACTIVE_PLAYER_FIELD_PVP_MEDALS, (uint)activeData.PvpMedals);
            for (int i = 0; i < 12; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_BUYBACK_PRICE;
                if (activeData.BuybackPrice[i] != null)
                    m_fields.SetUpdateField<uint>(startIndex + i, (uint)activeData.BuybackPrice[i]!);
            }
            for (int i = 0; i < 12; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_BUYBACK_TIMESTAMP;
                if (activeData.BuybackTimestamp[i] != null)
                    m_fields.SetUpdateField<uint>(startIndex + i, (uint)activeData.BuybackTimestamp[i]!);
            }
            if (activeData.TodayHonorableKills != null && activeData.TodayDishonorableKills != null)
            {
                m_fields.SetUpdateField<ushort>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES_2, (ushort)activeData.TodayHonorableKills, 0);
                m_fields.SetUpdateField<ushort>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES_2, (ushort)activeData.TodayDishonorableKills, 1);
            }
            if (activeData.YesterdayHonorableKills != null && activeData.YesterdayDishonorableKills != null)
            {
                m_fields.SetUpdateField<ushort>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES_3, (ushort)activeData.YesterdayHonorableKills, 0);
                m_fields.SetUpdateField<ushort>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES_3, (ushort)activeData.YesterdayDishonorableKills, 1);
            }
            if (activeData.LastWeekHonorableKills != null && activeData.LastWeekDishonorableKills != null)
            {
                m_fields.SetUpdateField<ushort>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES_4, (ushort)activeData.LastWeekHonorableKills, 0);
                m_fields.SetUpdateField<ushort>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES_4, (ushort)activeData.LastWeekDishonorableKills, 1);
            }
            if (activeData.ThisWeekHonorableKills != null && activeData.ThisWeekDishonorableKills != null)
            {
                m_fields.SetUpdateField<ushort>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES_5, (ushort)activeData.ThisWeekHonorableKills, 0);
                m_fields.SetUpdateField<ushort>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES_5, (ushort)activeData.ThisWeekDishonorableKills, 1);
            }
            if (activeData.ThisWeekContribution != null)
                m_fields.SetUpdateField<uint>(ActivePlayerField.ACTIVE_PLAYER_FIELD_THIS_WEEK_CONTRIBUTION, (uint)activeData.ThisWeekContribution);
            if (activeData.LifetimeHonorableKills != null)
                m_fields.SetUpdateField<uint>(ActivePlayerField.ACTIVE_PLAYER_FIELD_LIFETIME_HONORABLE_KILLS, (uint)activeData.LifetimeHonorableKills);
            if (activeData.LifetimeDishonorableKills != null)
                m_fields.SetUpdateField<uint>(ActivePlayerField.ACTIVE_PLAYER_FIELD_LIFETIME_DISHONORABLE_KILLS, (uint)activeData.LifetimeDishonorableKills);
            if (activeData.YesterdayContribution != null)
                m_fields.SetUpdateField<uint>(ActivePlayerField.ACTIVE_PLAYER_FIELD_YESTERDAY_CONTRIBUTION, (uint)activeData.YesterdayContribution);
            if (activeData.LastWeekContribution != null)
                m_fields.SetUpdateField<uint>(ActivePlayerField.ACTIVE_PLAYER_FIELD_LAST_WEEK_CONTRIBUTION, (uint)activeData.LastWeekContribution);
            if (activeData.LastWeekRank != null)
                m_fields.SetUpdateField<uint>(ActivePlayerField.ACTIVE_PLAYER_FIELD_LAST_WEEK_RANK, (uint)activeData.LastWeekRank);
            if (activeData.WatchedFactionIndex != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_WATCHED_FACTION_INDEX, (int)activeData.WatchedFactionIndex);
            for (int i = 0; i < 32; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_COMBAT_RATING;
                if (activeData.CombatRatings[i] != null)
                    m_fields.SetUpdateField<int>(startIndex + i, (int)activeData.CombatRatings[i]!);
            }
            for (int i = 0; i < 6; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_ARENA_TEAM_INFO;
                int sizePerEntry = 12;
                if (activeData.PvpInfo[i] != null)
                {
                    m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry, (uint)activeData.PvpInfo[i].WeeklyPlayed);
                    m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 1, (uint)activeData.PvpInfo[i].WeeklyWon);
                    m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 2, (uint)activeData.PvpInfo[i].SeasonPlayed);
                    m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 3, (uint)activeData.PvpInfo[i].SeasonWon);
                    m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 4, (uint)activeData.PvpInfo[i].Rating);
                    m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 5, (uint)activeData.PvpInfo[i].WeeklyBestRating);
                    m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 6, (uint)activeData.PvpInfo[i].SeasonBestRating);
                    m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 7, (uint)activeData.PvpInfo[i].PvpTierID);
                    m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 8, (uint)activeData.PvpInfo[i].WeeklyBestWinPvpTierID);
                    m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 9, (uint)activeData.PvpInfo[i].Field_28);
                    m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 10, (uint)activeData.PvpInfo[i].Field_2C);
                    m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 11, (uint)(activeData.PvpInfo[i].Disqualified ? 1 : 0));
                }
            }
            if (activeData.MaxLevel != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_MAX_LEVEL, (int)activeData.MaxLevel);
            if (activeData.ScalingPlayerLevelDelta != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_SCALING_PLAYER_LEVEL_DELTA, (int)activeData.ScalingPlayerLevelDelta);
            if (activeData.MaxCreatureScalingLevel != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_MAX_CREATURE_SCALING_LEVEL, (int)activeData.MaxCreatureScalingLevel);
            for (int i = 0; i < 4; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_NO_REAGENT_COST;
                if (activeData.NoReagentCostMask[i] != null)
                    m_fields.SetUpdateField<uint>(startIndex + i, (uint)activeData.NoReagentCostMask[i]!);
            }
            if (activeData.PetSpellPower != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_PET_SPELL_POWER, (int)activeData.PetSpellPower);
            for (int i = 0; i < 2; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_PROFESSION_SKILL_LINE;
                if (activeData.ProfessionSkillLine[i] != null)
                    m_fields.SetUpdateField<int>(startIndex + i, (int)activeData.ProfessionSkillLine[i]!);
            }
            if (activeData.UiHitModifier != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_UI_HIT_MODIFIER, (float)activeData.UiHitModifier);
            if (activeData.UiSpellHitModifier != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_UI_SPELL_HIT_MODIFIER, (float)activeData.UiSpellHitModifier);
            if (activeData.HomeRealmTimeOffset != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_HOME_REALM_TIME_OFFSET, (int)activeData.HomeRealmTimeOffset);
            if (activeData.ModPetHaste != null)
                m_fields.SetUpdateField<float>(ActivePlayerField.ACTIVE_PLAYER_FIELD_MOD_PET_HASTE, (float)activeData.ModPetHaste);
            if (activeData.LocalRegenFlags != null || activeData.AuraVision != null || activeData.NumBackpackSlots != null)
            {
                if (activeData.LocalRegenFlags != null)
                    m_fields.SetUpdateField<byte>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES_6, (byte)activeData.LocalRegenFlags, 0);
                if (activeData.AuraVision != null)
                    m_fields.SetUpdateField<byte>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES_6, (byte)activeData.AuraVision, 1);
                if (activeData.NumBackpackSlots != null)
                    m_fields.SetUpdateField<byte>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES_6, (byte)activeData.NumBackpackSlots, 2);
            }
            if (activeData.OverrideSpellsID != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_OVERRIDE_SPELLS_ID, (int)activeData.OverrideSpellsID);
            if (activeData.LfgBonusFactionID != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_LFG_BONUS_FACTION_ID, (int)activeData.LfgBonusFactionID);
            if (activeData.LootSpecID != null)
                m_fields.SetUpdateField<uint>(ActivePlayerField.ACTIVE_PLAYER_FIELD_LOOT_SPEC_ID, (uint)activeData.LootSpecID);
            if (activeData.OverrideZonePVPType != null)
                m_fields.SetUpdateField<uint>(ActivePlayerField.ACTIVE_PLAYER_FIELD_OVERRIDE_ZONE_PVP_TYPE, (uint)activeData.OverrideZonePVPType);
            for (int i = 0; i < 4; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_BAG_SLOT_FLAGS;
                if (activeData.BagSlotFlags[i] != null)
                    m_fields.SetUpdateField<uint>(startIndex + i, (uint)activeData.BagSlotFlags[i]!);
            }
            for (int i = 0; i < 7; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_BANK_BAG_SLOT_FLAGS;
                if (activeData.BankBagSlotFlags[i] != null)
                    m_fields.SetUpdateField<uint>(startIndex + i, (uint)activeData.BankBagSlotFlags[i]!);
            }
            for (int i = 0; i < 875; i++)
            {
                int startIndex = (int)ActivePlayerField.ACTIVE_PLAYER_FIELD_QUEST_COMPLETED;
                if (activeData.QuestCompleted[i] != null)
                    m_fields.SetUpdateField<ulong>(startIndex + i * 2, (ulong)activeData.QuestCompleted[i]!);
            }
            if (activeData.Honor != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_HONOR, (int)activeData.Honor);
            if (activeData.HonorNextLevel != null)
                m_fields.SetUpdateField<int>(ActivePlayerField.ACTIVE_PLAYER_FIELD_HONOR_NEXT_LEVEL, (int)activeData.HonorNextLevel);
            if (activeData.PvPTierMaxFromWins != null)
                m_fields.SetUpdateField<uint>(ActivePlayerField.ACTIVE_PLAYER_FIELD_PVP_TIER_MAX_FROM_WINS, (uint)activeData.PvPTierMaxFromWins);
            if (activeData.PvPLastWeeksTierMaxFromWins != null)
                m_fields.SetUpdateField<uint>(ActivePlayerField.ACTIVE_PLAYER_FIELD_PVP_LAST_WEEKS_TIER_MAX_FROM_WINS, (uint)activeData.PvPLastWeeksTierMaxFromWins);
            if (activeData.InsertItemsLeftToRight != null || activeData.PvPRankProgress != null)
            {
                if (activeData.InsertItemsLeftToRight != null)
                    m_fields.SetUpdateField<byte>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES_7, (byte)(activeData.InsertItemsLeftToRight == true ? 1 : 0), 0);
                if (activeData.PvPRankProgress != null)
                    m_fields.SetUpdateField<byte>(ActivePlayerField.ACTIVE_PLAYER_FIELD_BYTES_7, (byte)activeData.PvPRankProgress, 1);
            }

            // Dynamic Fields
            if (activeData.SelfResSpells != null)
            {
                uint[] fields = new uint[activeData.SelfResSpells.Count];
                for (int i = 0; i < activeData.SelfResSpells.Count; i++)
                    fields[i] = activeData.SelfResSpells[i];
                m_dynamicFields.SetUpdateField((int)ActivePlayerDynamicField.ACTIVE_PLAYER_DYNAMIC_FIELD_SELF_RES_SPELLS, fields, DynamicFieldChangeType.ValueAndSizeChanged);
            }
            if (activeData.HasDailyQuestsUpdate)
            {
                uint[] fields = new uint[m_gameState.DailyQuestsDone.Count];
                int counter = 0;
                foreach (var itr in m_gameState.DailyQuestsDone)
                    fields[counter++] = itr.Value;
                m_dynamicFields.SetUpdateField((int)ActivePlayerDynamicField.ACTIVE_PLAYER_DYNAMIC_FIELD_DAILY_QUESTS_COMPLETED, fields, DynamicFieldChangeType.ValueAndSizeChanged);
            }
        }

        GameObjectData goData = m_updateData.GameObjectData;
        if (goData != null)
        {
            if (goData.CreatedBy != null)
                m_fields.SetUpdateField(GameObjectField.GAMEOBJECT_FIELD_CREATED_BY, goData.CreatedBy.Value);
            if (goData.DisplayID != null)
                m_fields.SetUpdateField<int>(GameObjectField.GAMEOBJECT_DISPLAYID, (int)goData.DisplayID);
            if (goData.Flags != null)
                m_fields.SetUpdateField<uint>(GameObjectField.GAMEOBJECT_FLAGS, (uint)goData.Flags);
            for (int i = 0; i < 4; i++)
            {
                int startIndex = (int)GameObjectField.GAMEOBJECT_PARENTROTATION;
                if (goData.ParentRotation[i] != null)
                    m_fields.SetUpdateField<float>(startIndex + i, (float)goData.ParentRotation[i]!);
            }
            if (goData.FactionTemplate != null)
                m_fields.SetUpdateField<int>(GameObjectField.GAMEOBJECT_FACTION, (int)goData.FactionTemplate);
            if (goData.Level != null)
                m_fields.SetUpdateField<int>(GameObjectField.GAMEOBJECT_LEVEL, (int)goData.Level);
            if (goData.State != null || goData.TypeID != null || goData.ArtKit != null || goData.PercentHealth != null)
            {
                if (goData.State != null)
                    m_fields.SetUpdateField<byte>(GameObjectField.GAMEOBJECT_BYTES_1, (byte)goData.State, 0);
                if (goData.TypeID != null)
                    m_fields.SetUpdateField<byte>(GameObjectField.GAMEOBJECT_BYTES_1, (byte)goData.TypeID, 1);
                if (goData.ArtKit != null)
                    m_fields.SetUpdateField<byte>(GameObjectField.GAMEOBJECT_BYTES_1, (byte)goData.ArtKit, 2);
                if (goData.PercentHealth != null)
                    m_fields.SetUpdateField<byte>(GameObjectField.GAMEOBJECT_BYTES_1, (byte)goData.PercentHealth, 3);
            }
            if (goData.SpellVisualID != null)
                m_fields.SetUpdateField<uint>(GameObjectField.GAMEOBJECT_SPELL_VISUAL_ID, (uint)goData.SpellVisualID);
            if (goData.StateSpellVisualID != null)
                m_fields.SetUpdateField<uint>(GameObjectField.GAMEOBJECT_STATE_SPELL_VISUAL_ID, (uint)goData.StateSpellVisualID);
            if (goData.StateAnimID != null)
                m_fields.SetUpdateField<uint>(GameObjectField.GAMEOBJECT_STATE_ANIM_ID, (uint)goData.StateAnimID);
            if (goData.StateAnimKitID != null)
                m_fields.SetUpdateField<uint>(GameObjectField.GAMEOBJECT_STATE_ANIM_KIT_ID, (uint)goData.StateAnimKitID);
            for (int i = 0; i < 4; i++)
            {
                int startIndex = (int)GameObjectField.GAMEOBJECT_STATE_WORLD_EFFECT_ID;
                if (goData.StateWorldEffectIDs[i] != null)
                    m_fields.SetUpdateField<uint>(startIndex + i, (uint)goData.StateWorldEffectIDs[i]!);
            }
            if (goData.CustomParam != null)
                m_fields.SetUpdateField<uint>(GameObjectField.GAMEOBJECT_FIELD_CUSTOM_PARAM, (uint)goData.CustomParam);
        }

        DynamicObjectData dynData = m_updateData.DynamicObjectData;
        if (dynData != null)
        {
            if (dynData.Caster != null)
                m_fields.SetUpdateField(DynamicObjectField.DYNAMICOBJECT_CASTER, dynData.Caster.Value);
            if (dynData.Type != null)
                m_fields.SetUpdateField<uint>(DynamicObjectField.DYNAMICOBJECT_TYPE, (uint)dynData.Type);
            if (dynData.SpellXSpellVisualID != null)
                m_fields.SetUpdateField<int>(DynamicObjectField.DYNAMICOBJECT_SPELL_X_SPELL_VISUAL_ID, (int)dynData.SpellXSpellVisualID);
            if (dynData.SpellID != null)
                m_fields.SetUpdateField<int>(DynamicObjectField.DYNAMICOBJECT_SPELLID, (int)dynData.SpellID);
            if (dynData.Radius != null)
                m_fields.SetUpdateField<float>(DynamicObjectField.DYNAMICOBJECT_RADIUS, (float)dynData.Radius);
            if (dynData.CastTime != null)
                m_fields.SetUpdateField<uint>(DynamicObjectField.DYNAMICOBJECT_CASTTIME, (uint)dynData.CastTime);
        }

        CorpseData corpseData = m_updateData.CorpseData;
        if (corpseData != null)
        {
            if (corpseData.Owner != null)
                m_fields.SetUpdateField(CorpseField.CORPSE_FIELD_OWNER, corpseData.Owner.Value);
            if (corpseData.PartyGUID != null)
                m_fields.SetUpdateField(CorpseField.CORPSE_FIELD_PARTY_GUID, corpseData.PartyGUID.Value);
            if (corpseData.GuildGUID != null)
                m_fields.SetUpdateField(CorpseField.CORPSE_FIELD_GUILD_GUID, corpseData.GuildGUID.Value);
            if (corpseData.DisplayID != null)
                m_fields.SetUpdateField<uint>(CorpseField.CORPSE_FIELD_DISPLAY_ID, (uint)corpseData.DisplayID);
            for (int i = 0; i < 19; i++)
            {
                int startIndex = (int)CorpseField.CORPSE_FIELD_ITEMS;
                if (corpseData.Items[i] != null)
                    m_fields.SetUpdateField<uint>(startIndex + i, (uint)corpseData.Items[i]!);
            }
            if (corpseData.RaceId != null || corpseData.SexId != null || corpseData.ClassId != null)
            {
                if (corpseData.RaceId != null)
                    m_fields.SetUpdateField<byte>(CorpseField.CORPSE_FIELD_BYTES_1, (byte)corpseData.RaceId, 0);
                if (corpseData.SexId != null)
                    m_fields.SetUpdateField<byte>(CorpseField.CORPSE_FIELD_BYTES_1, (byte)corpseData.SexId, 1);
                if (corpseData.ClassId != null)
                    m_fields.SetUpdateField<byte>(CorpseField.CORPSE_FIELD_BYTES_1, (byte)corpseData.ClassId, 2);
            }
            if (corpseData.Flags != null)
                m_fields.SetUpdateField<uint>(CorpseField.CORPSE_FIELD_FLAGS, (uint)corpseData.Flags);
            if (corpseData.DynamicFlags != null)
                m_fields.SetUpdateField<uint>(CorpseField.CORPSE_FIELD_DYNAMIC_FLAGS, (uint)corpseData.DynamicFlags);
            if (corpseData.FactionTemplate != null)
                m_fields.SetUpdateField<int>(CorpseField.CORPSE_FIELD_FACTION_TEMPLATE, (int)corpseData.FactionTemplate);
            for (int i = 0; i < 35; i++)
            {
                int startIndex = (int)CorpseField.CORPSE_FIELD_CUSTOMIZATION_CHOICES;
                int sizePerEntry = 2;
                if (corpseData.Customizations[i] != null)
                {
                    m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry, (uint)corpseData.Customizations[i].ChrCustomizationOptionID);
                    m_fields.SetUpdateField<uint>(startIndex + i * sizePerEntry + 1, (uint)corpseData.Customizations[i].ChrCustomizationChoiceID);
                }
            }
        }

        m_alreadyWritten = true;
    }


}
