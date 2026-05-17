using System.Runtime.InteropServices;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// One flat record from <see cref="ISaveLoader.ListCharacterRefs"/>.
/// <c>repr(C)</c> blittable struct with the exact 16-byte layout the
/// Rust C ABI's <c>CrimsonCharacterRefRecord</c> emits — see the
/// upstream doc in <c>vendor/crimson-rs/src/c_abi/mod.rs</c>.
///
/// <para>
/// One record is emitted per present <c>CharacterKey</c>-typed field
/// occurrence found while walking every top-level block + every
/// ObjectList / Locator descendant. Scalar fields produce one row;
/// <c>DynamicArray</c>s of <c>CharacterKey</c> produce one row per
/// element. <see cref="BlockIndex"/> always names the OUTER
/// top-level block — nested elements roll up to their owning block.
/// </para>
///
/// <para>
/// <b>Linkage caveat</b>: A given <c>CharacterKey</c> value can mean
/// different concrete entities depending on the surrounding
/// schema field's role (spawn marker vs friendly mercenary vs
/// quest target vs cosmetic preview). This flat list enumerates
/// every <i>schema-tagged</i> reference and leaves cross-verification
/// to the consumer.
/// </para>
///
/// <para>
/// <b>Validity window</b>: <see cref="BlockIndex"/> stays valid as
/// long as the loaded save's top-level block layout hasn't shifted
/// (block adds / removes are rare — typically only after very
/// large-scale mutations). Pair snapshots with
/// <see cref="ISaveLoader.GetMutationVersion"/> for O(1) staleness
/// detection.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public readonly struct CharacterRefRecord
{
    /// <summary>
    /// Top-level <c>ObjectBlock</c> index containing this reference.
    /// Pass to <c>LoadBlockDetails</c> for the full block payload;
    /// the consumer then drills into the specific field manually.
    /// </summary>
    public readonly uint BlockIndex;

    /// <summary>
    /// The <c>CharacterKey</c> value read out of the schema field —
    /// the u32 the consumer joins with
    /// <see cref="CrimsonAtomtic.Ui.Services.LocalizationProvider.LookupCharacterInternalName"/>
    /// and friends.
    /// </summary>
    public readonly uint CharacterKey;

    /// <summary>
    /// Class index of the top-level block named by
    /// <see cref="BlockIndex"/> — handy for client-side filtering
    /// without a second <c>LoadBlockDetails</c> round-trip.
    /// </summary>
    public readonly uint ClassIndex;

    /// <summary>Reserved padding to keep the struct at 16 bytes for natural alignment. Always 0.</summary>
    public readonly uint Reserved0;
}
