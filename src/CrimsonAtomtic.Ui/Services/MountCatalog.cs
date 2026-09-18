namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// How a given mount is unlocked. The two real-world categories Crimson
/// Desert uses (settled by the 2026-05-31 slot101-vs-slot105 sigil-unlock
/// diff — see <c>docs/status.md</c>):
/// </summary>
public enum MountUnlockKind
{
    /// <summary>
    /// Sigil-gated. The legitimate unlock is to craft+use a
    /// <c>Sigil of Solidarity (&lt;mount&gt;)</c> item; the engine then does
    /// the full unlock on use (merc element + alert + riding). Our path:
    /// grant the sigil item into Quest Artifacts, the player uses it
    /// in-game. No knowledge injection needed for these.
    /// </summary>
    SigilGrant,

    /// <summary>
    /// Non-sigil special mount (the dragon). Unlock = transplant the
    /// mount's real <c>_mercenaryDataList</c> element from a save that owns
    /// it (a charKey swap on a generic clone CTDs) + inject its identity /
    /// riding knowledge into <c>KnowledgeSaveData._list</c>.
    /// </summary>
    DragonTransplant,
}

/// <summary>One unlockable mount and the data its unlock path needs.</summary>
/// <param name="DisplayName">English fallback label. For sigil mounts the
/// dialog prefers the localized sigil-item name resolved via
/// <see cref="SigilItemKey"/>; this is the safety net.</param>
/// <param name="Kind">Which unlock path applies.</param>
/// <param name="CharacterKey">The mount's <c>_characterKey</c>. Verified for
/// the dragon (1000799) and Silver Fang (1003918); <c>0</c> = not needed by
/// the unlock path (sigil mounts grant by item only) and left unverified.</param>
/// <param name="SigilItemKey">The <c>Sigil of Solidarity</c> itemKey for
/// <see cref="MountUnlockKind.SigilGrant"/>; <c>0</c> otherwise.</param>
/// <param name="IsPet">Cosmetic: the wheel it shows in (pet vs special
/// mount). Phoenix is a pet; the rest are special mounts.</param>
public sealed record MountEntry(
    string DisplayName,
    MountUnlockKind Kind,
    uint CharacterKey,
    uint SigilItemKey,
    bool IsPet);

/// <summary>
/// Static catalog of editor-unlockable mounts + the constants the
/// mount-unlock flow needs. Centralized magic-strings file (Mandatory
/// rule 7). Item / character keys are from <c>iteminfo</c> /
/// <c>characterinfo</c>, confirmed working in-game (see status.md).
/// </summary>
public static class MountCatalog
{
    /// <summary>
    /// Quest Artifacts inventory container key — where a
    /// <c>Sigil of Solidarity</c> lives (NOT the Backpack / Camp). The
    /// engine's "use sigil" path reads it out of this container.
    /// </summary>
    public const uint QuestArtifactsInventoryKey = 5;

    /// <summary>The dragon's <c>_characterKey</c> ("深暗之星" / Blackstar).</summary>
    public const uint DragonCharacterKey = 1000799;

    /// <summary>
    /// The dragon's base max HP. The captured element is mid-fight
    /// (<c>_currentHp</c> 1032; the game showed 1038/2500), so the unlock
    /// fills <c>_currentHp</c> to this. Cross-confirmed by the reference
    /// editor's full-HP <c>DRAGON_HEX</c>, which carries 2500 (0x09C4) in
    /// the same field. <c>_currentHp</c> is a plain u64: it only looked like
    /// a packed TStat (<c>01 00 01 01 01 [u16] 00</c>) while the decoder
    /// misread the one-byte markers of the absent lists in front of it.
    /// </summary>
    public const ulong DragonFullHp = 2500;

    /// <summary>
    /// The dragon's real <c>_mercenaryDataList</c> element as a crimson-rs
    /// element template ("CRET" v1, hex), inserted with
    /// <c>ISaveLoader.ListInsertElementTemplate</c>. A charKey swap on a
    /// generic clone CTDs, so the real element content is required.
    ///
    /// <para>The template records the element by field NAME, so it is
    /// rebuilt under whichever schema the target save carries. Raw element
    /// bytes only fit the schema that wrote them, and <c>MercenarySaveData</c>
    /// keeps changing: 2.00 removed <c>_occupationState</c> (the builder
    /// drops it), 2.01 appended <c>_shipStationSaveList</c> (left absent),
    /// and <c>ExperienceLevelSaveData</c> gained
    /// <c>_shareKnowledgeRewardDailyCountData</c> (created empty).</para>
    ///
    /// <para>Exported from the 212-byte element first captured from a save
    /// that owns the dragon, decoded under that save's schema:
    /// <c>_characterKey</c> 1000799, <c>_mercenaryNo</c> 640,
    /// <c>_ownedCharacterKey</c> 1, <c>_levelData</c> (three empty
    /// <c>FriendlyDailyCountSaveData</c>), <c>_lastPaidTime</c> /
    /// <c>_lastBreedingTime</c> 3420870048, <c>_lastSummoned</c>, spawn
    /// position / yaw / field key 1, <c>_isMainMercenary</c>,
    /// <c>_isInitialize</c>, <c>_occupationState</c> 0, <c>_currentHp</c>
    /// 1032, <c>_currentMp</c> 0. The unlock then renumbers
    /// <c>_mercenaryNo</c>, clears <c>_isMainMercenary</c> and fills HP.</para>
    /// </summary>
    public const string DragonElementTemplateHex =
        "435245540111004d657263656e61727953617665446174610000000000ffffff" +
        "ffffffffff0f000d005f6368617261637465724b65790004005f450f000c005f" +
        "6d657263656e6172794e6f000800800200000000000012005f6f776e65644368" +
        "617261637465724b6579000400010000000a005f6c6576656c44617461030400" +
        "1700457870657269656e63654c6576656c53617665446174610000000000ffff" +
        "ffffffffffff03001b005f67696d6d69636b4576656e744461696c79436f756e" +
        "74446174610304001a00467269656e646c794461696c79436f756e7453617665" +
        "446174610000000000ffffffffffffffff00001f005f616374696f6e4672616d" +
        "654576656e744461696c79436f756e74446174610304001a00467269656e646c" +
        "794461696c79436f756e7453617665446174610000000000ffffffffffffffff" +
        "000018005f74616c6b4576656e744461696c79436f756e74446174610304001a" +
        "00467269656e646c794461696c79436f756e7453617665446174610000000000" +
        "ffffffffffffffff00000d005f6c6173745061696454696d65000800a055e6cb" +
        "0000000011005f6c6173744272656564696e6754696d65000800a055e6cb0000" +
        "00000d005f6c61737453756d6d6f6e6564000100010e005f737061776e506f73" +
        "6974696f6e000c002c0c22c6fb671a44821d79c509005f737061776e59617700" +
        "0400b46d83be12005f737061776e4669656c64496e666f4b6579000400010000" +
        "0010005f69734d61696e4d657263656e617279000100010d005f6973496e6974" +
        "69616c697a650001000110005f6f636375706174696f6e537461746500010000" +
        "0a005f63757272656e74487000080008040000000000000a005f63757272656e" +
        "744d700008000000000000000000";

    /// <summary>
    /// The 187-key dragon ("Blackstar") knowledge set — the
    /// <b>"no-quests"</b> unlock bundle: add the real merc element + inject
    /// these keys, NO quest-state changes. This is the exact set the
    /// reference editor's <c>_unlock_dragon_mount_no_quests()</c> ships (and
    /// the one a prior RE session confirmed in-game: "element + this set →
    /// summons + rides"). It is curated riding/dragon/skills/UI + the
    /// dragon-questline world knowledge the engine's summon path checks —
    /// NOT animal-riding and NOT a wholesale dump, so it won't unlock
    /// achievements. The inject filters out keys the save already has, so a
    /// player who already did the dragon questline only gets the gaps.
    ///
    /// <para>Includes the obvious gates <c>1000560</c>
    /// (Knowledge_Unique_Varnia_Dragon / "Blackstar") and <c>1000174</c>
    /// (Knowledge_CallVehicle / "Summon Mount"), plus 185 more. Extracted
    /// verbatim from the reference's no-quests list — do not prune by
    /// name-guessing (an earlier 2-key guess made the dragon show but
    /// not summon).</para>
    /// </summary>
    public static readonly uint[] DragonKnowledgeKeys =
    [
        40038u, 1000174u, 1000175u, 1000187u, 1000189u, 1000697u, 1000720u, 1000948u,
        1001892u, 1003893u, 1004138u, 1004154u, 1004176u, 1004177u, 1004178u, 2147483119u,
        2147483121u, 2147483122u, 2147483123u, 2147483124u, 2147483125u, 2147483126u, 2147483127u, 2147483128u,
        2147483130u, 2147483131u, 2147483132u, 2147483133u, 2147483134u, 2147483135u, 2601u, 2602u,
        2603u, 2617u, 2618u, 1000560u, 1001083u, 1003311u, 40001u, 40002u,
        40003u, 40012u, 40013u, 40014u, 40018u, 40024u, 40028u, 40030u,
        40034u, 40036u, 40039u, 40048u, 40063u, 40064u, 40065u, 40068u,
        40069u, 40071u, 40072u, 40082u, 40086u, 40089u, 40090u, 40091u,
        40114u, 1000000u, 1000001u, 1000013u, 1000014u, 1000024u, 1000034u, 1000037u,
        1000100u, 1000101u, 1000109u, 1000134u, 1000137u, 1000138u, 1000210u, 1000230u,
        1000490u, 1000493u, 1000908u, 1000929u, 1001116u, 1001117u, 1001710u, 1001744u,
        1001756u, 1001760u, 1001789u, 1002348u, 1002349u, 1002351u, 1002352u, 1002710u,
        1002741u, 1002743u, 1003088u, 1003090u, 1003108u, 1003245u, 1003269u, 1003273u,
        1003274u, 1003279u, 1003346u, 1003359u, 1003482u, 1003505u, 1003508u, 1003512u,
        1003513u, 1003518u, 1003519u, 1003521u, 1003522u, 1003523u, 1003524u, 1003525u,
        1003571u, 1000372u, 1000375u, 1000738u, 1001290u, 1001297u, 1001298u, 1001434u,
        1001435u, 1001436u, 1001453u, 1001463u, 1001464u, 1001465u, 1001541u, 1001542u,
        1001550u, 1001553u, 1001704u, 1002592u, 1003325u, 1003334u, 1003341u, 1003467u,
        1003468u, 1003470u, 1003472u, 1003473u, 1003474u, 1003475u, 1003476u, 1003477u,
        1003480u, 1003481u, 1003492u, 1003494u, 1003495u, 1003500u, 1003501u, 1003502u,
        1003503u, 1003504u, 1003507u, 1003510u, 1000070u, 1000574u, 1001100u, 1001422u,
        1000297u, 1000464u, 1000776u, 1001287u, 1001511u, 1001516u, 2147483447u, 2147483454u,
        1001304u, 1001664u, 1001692u, 1001695u, 1001700u, 1001895u, 1001897u, 1001651u,
        1002833u, 1003658u, 1003790u,
    ];

    /// <summary>
    /// The unlockable mounts. Six sigil-gated (granted as an item) + the
    /// dragon (transplant + knowledge).
    /// </summary>
    public static readonly IReadOnlyList<MountEntry> All =
    [
        // ── Sigil-gated (grant the sigil, use in-game) ──
        new("White Bear",          MountUnlockKind.SigilGrant, 0,       1003843, IsPet: false),
        new("Silver Fang",         MountUnlockKind.SigilGrant, 1003918, 1003844, IsPet: false),
        new("Snowwhite Deer",      MountUnlockKind.SigilGrant, 0,       1003845, IsPet: false),
        new("Alpine Ibex",         MountUnlockKind.SigilGrant, 0,       1003846, IsPet: false),
        new("Rock Tusk Warthog",   MountUnlockKind.SigilGrant, 0,       1003847, IsPet: false),
        new("Phoenix",             MountUnlockKind.SigilGrant, 0,       1003921, IsPet: true),

        // ── Dragon (transplant the real element + inject knowledge) ──
        new("Blackstar (Dragon)",  MountUnlockKind.DragonTransplant, DragonCharacterKey, 0, IsPet: false),
    ];
}
