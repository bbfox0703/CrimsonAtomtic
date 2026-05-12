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
public sealed partial class SaveModelJsonContext : JsonSerializerContext;
