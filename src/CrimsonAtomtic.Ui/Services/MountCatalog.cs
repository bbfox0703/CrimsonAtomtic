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
    /// The dragon's base max HP. The donor element is captured mid-fight
    /// (1038/2500), so after grafting we fill <c>_currentHp</c> to this. The
    /// value is cross-confirmed: the reference editor's full-HP
    /// <c>DRAGON_HEX</c> carries 2500 (0x09C4) at the same <c>_currentHp</c>
    /// slot. The field is a packed TStat (<c>01 00 01 01 01 [u16 current] 00</c>);
    /// we set only the inner current-HP u16, leaving the rest untouched.
    /// </summary>
    public const ushort DragonFullHp = 2500;

    /// <summary>
    /// The dragon's real <c>_mercenaryDataList</c> element, captured once from
    /// a save that owns it (212 bytes, hex). Inserted via
    /// <c>ISaveLoader.ListInsertElement</c> after remapping its embedded
    /// schema type-indices to the target save (see
    /// <see cref="DragonElementTypeIndexFixups"/>) — this replaces the old
    /// 1.47 MB whole-save donor embed. A charKey swap on a generic clone
    /// CTDs, so the real element content is required.
    /// </summary>
    public const string DragonElementHex =
        "06000d19003f0806370000ffffffffffffffff96ae1200000000005f450f00" +
        "80020000000000000100000001001c390000ffffffffffffffffbcae120000" +
        "0000000100002d0000ffffffffffffffffd2ae12000000000004000000" +
        "0100002d0000ffffffffffffffffecae12000000000004000000" +
        "0100002d0000ffffffffffffffff06af1200000000000400000052000000" +
        "a055e6cb00000000a055e6cb00000000012c0c22c6fb671a44821d79c5" +
        "b46d83be01000000010101010001010108040000000000000000000000" +
        "000000b9000000";

    /// <summary>
    /// Where the type-indices live inside <see cref="DragonElementHex"/> and
    /// the class each one names. At unlock time we read the target save's
    /// type-index for each class (from one of its own merc elements) and
    /// overwrite the u16 at each offset — the same class-name → type-index
    /// remap <c>crimson_save_transplant_list_element</c> does internally, but
    /// for a byte blob. Offsets are byte positions from the element start;
    /// values are little-endian u16. Source indices in the captured bytes are
    /// Mercenary=55, ExperienceLevel=57, FriendlyDailyCount=45.
    /// </summary>
    public static readonly (int Offset, string ClassName)[] DragonElementTypeIndexFixups =
    [
        (8,   "MercenarySaveData"),
        (46,  "ExperienceLevelSaveData"),
        (68,  "FriendlyDailyCountSaveData"),
        (94,  "FriendlyDailyCountSaveData"),
        (120, "FriendlyDailyCountSaveData"),
    ];

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
