using System.Text.Json.Serialization;

namespace CrimsonAtomtic.SaveModel;

/// <summary>
/// High-level summary of a parsed save file. Built from
/// <c>crimson_rs::save::Save</c> plus
/// <c>crimson_rs::save::Body</c> on the Rust side; the UI binds to this
/// shape rather than the raw FFI types.
/// </summary>
public sealed record SaveSummary(
    string Source,
    string SlotName,
    int Version,
    int Flags,
    long PayloadSize,
    long UncompressedSize,
    bool HmacOk,
    int SchemaTypeCount,
    int TocEntryCount,
    long TotalBlockBytes,
    IReadOnlyList<BlockSummary> Blocks);

/// <summary>One-line summary of an <c>ObjectBlock</c>.</summary>
public sealed record BlockSummary(
    int Index,
    int ClassIndex,
    string ClassName,
    long DataOffset,
    long DataSize,
    int FieldsPresent,
    int FieldsDecoded);

/// <summary>
/// Full per-field decode of one block, populated lazily by
/// <c>NativeSaveLoader.LoadBlockDetails</c> when the user selects a row.
/// Mirrors the JSON document returned by
/// <c>crimson_save_get_block_json</c>.
/// </summary>
public sealed record BlockDetails(
    int ClassIndex,
    string ClassName,
    long DataOffset,
    long DataSize,
    string MaskBytesHex,
    string TrailingPadHex,
    IReadOnlyList<DecodedFieldRow> Fields,
    IReadOnlyList<long[]> UndecodedRanges);

/// <summary>
/// One field within a <see cref="BlockDetails"/>. <c>Value</c> is a
/// pre-formatted human string emitted by the Rust side, mirroring
/// <c>tools/inspect/inspect_save_section.py --pretty</c>.
/// </summary>
/// <remarks>
/// <para>
/// For nested data, exactly one of <see cref="Child"/> and
/// <see cref="Elements"/> is populated (or neither, for scalars):
/// </para>
/// <list type="bullet">
///   <item><c>Kind == "object_locator"</c> with an inline child → <c>Child</c> set.</item>
///   <item><c>Kind == "object_list"</c> → <c>Elements</c> set (possibly empty).</item>
///   <item>Other kinds → both null.</item>
/// </list>
/// </remarks>
public sealed record DecodedFieldRow(
    int FieldIndex,
    string Name,
    string TypeName,
    int MetaKind,
    int MetaSize,
    long MetaAux,
    bool Present,
    string Kind,
    string Value,
    long Start,
    long End,
    string Note,
    BlockDetails? Child,
    IReadOnlyList<BlockDetails>? Elements)
{
    /// <summary>True when this field has nested data the UI can drill into.</summary>
    public bool HasNested => Child is not null || (Elements is { Count: > 0 });
}

/// <summary>
/// Source-generated JSON context. Required for AOT — System.Text.Json
/// uses reflection at runtime otherwise, and reflection is incompatible
/// with the trimmed AOT build.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SaveSummary))]
[JsonSerializable(typeof(BlockSummary))]
[JsonSerializable(typeof(IReadOnlyList<BlockSummary>))]
[JsonSerializable(typeof(BlockDetails))]
[JsonSerializable(typeof(DecodedFieldRow))]
[JsonSerializable(typeof(IReadOnlyList<DecodedFieldRow>))]
[JsonSerializable(typeof(IReadOnlyList<BlockDetails>))]
[JsonSerializable(typeof(IReadOnlyList<long[]>))]
[JsonSerializable(typeof(long[]))]
public sealed partial class SaveModelJsonContext : JsonSerializerContext;
