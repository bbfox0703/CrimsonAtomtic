using System.Globalization;

namespace CrimsonAtomtic.SaveModel;

/// <summary>
/// Pure helpers that bridge the pre-formatted scalar <see cref="DecodedFieldRow.Value"/>
/// strings emitted by the Rust C ABI ("32492971 &lt;u32&gt;", "true &lt;bool&gt;",
/// "1.5 &lt;f32&gt;", …) and the little-endian byte buffers the
/// <c>crimson_save_set_scalar_field</c> entry point expects.
/// </summary>
/// <remarks>
/// <para>
/// Rust's <c>format_field_value</c> (in <c>c_abi/mod.rs</c>) joins the raw
/// printed value and its type tag with a space, e.g. <c>"32492971 &lt;u32&gt;"</c>.
/// We split on the final space and validate the trailing <c>&lt;…&gt;</c>
/// shape. Anything else (e.g. <c>"(absent)"</c>, <c>"-&gt; Foo (offset N)"</c>)
/// returns <c>false</c> from <see cref="TryParse"/>, signalling the field is
/// not editable through the scalar surface.
/// </para>
/// <para>
/// Bytes scalars (<c>"100 bytes &lt;bytes&gt;"</c>) parse cleanly but
/// <see cref="IsTextEditable"/> reports <c>false</c> — opaque byte sequences
/// need a hex / binary editor, not a single textbox. They can still be
/// patched through the lower-level <c>SetScalarField(blockIndex, fieldIndex, bytes)</c>
/// API.
/// </para>
/// </remarks>
public static class ScalarFieldEditing
{
    /// <summary>The set of type tags accepted by <see cref="TryEncode"/>.</summary>
    public static readonly IReadOnlySet<string> SupportedTypeTags =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "bool", "u8", "u16", "u32", "u64",
            "i8", "i16", "i32", "i64",
            "f32", "f64",
        };

    /// <summary>
    /// True when the field is a fixed-prefix or fixed-suffix scalar whose
    /// type tag has a textual representation we can round-trip
    /// (<see cref="TryEncode"/> returns true).
    /// </summary>
    public static bool IsTextEditable(DecodedFieldRow row)
    {
        if (!IsScalarKind(row.Kind))
        {
            return false;
        }
        return TryParse(row.Value, out _, out var tag)
            && SupportedTypeTags.Contains(tag);
    }

    /// <summary>
    /// True when the field's kind is one of the two byte-patchable scalar
    /// kinds. Bytes-scalars hit this path too; only <see cref="IsTextEditable"/>
    /// filters them further.
    /// </summary>
    public static bool IsScalarKind(string kind) =>
        kind == "fixed_prefix" || kind == "fixed_suffix";

    /// <summary>
    /// Derive an editable type tag from a row's schema <c>TypeName</c>
    /// even when the field is absent (no <c>"&lt;tag&gt;"</c> in its
    /// formatted value to parse). The Rust side erases the underlying
    /// scalar kind to <c>FieldKind::Absent</c> on absent rows, so
    /// promote-an-absent-field flows can't lean on
    /// <see cref="TryParse"/>; this helper fills the gap.
    /// </summary>
    /// <remarks>
    /// Handles primitive aliases (<c>uint64</c> ↔ <c>u64</c>, <c>float</c>
    /// ↔ <c>f32</c>, …) and the schema's <c>*Key</c> typedefs (all of
    /// which are 4-byte u32 in 1.06: ItemKey, MissionKey, QuestKey,
    /// StageKey, KnowledgeKey, FactionKey, CharacterKey, …). Vector
    /// types like <c>float3</c> / <c>float4</c> are intentionally
    /// rejected — a single textbox can't drive a multi-component value.
    /// </remarks>
    public static bool TryInferTypeTagFromSchema(string typeName, int metaSize, out string typeTag)
    {
        typeTag = string.Empty;
        if (string.IsNullOrEmpty(typeName))
        {
            return false;
        }
        switch (typeName)
        {
            case "bool":                          typeTag = "bool"; return true;
            case "uint8":  case "u8":  case "byte":  typeTag = "u8";  return true;
            case "uint16": case "u16":               typeTag = "u16"; return true;
            case "uint32": case "u32":               typeTag = "u32"; return true;
            case "uint64": case "u64":               typeTag = "u64"; return true;
            case "int8":   case "i8":  case "sbyte": typeTag = "i8";  return true;
            case "int16":  case "i16":               typeTag = "i16"; return true;
            case "int32":  case "i32":               typeTag = "i32"; return true;
            case "int64":  case "i64":               typeTag = "i64"; return true;
            case "float":  case "f32": case "single": typeTag = "f32"; return true;
            case "double": case "f64":               typeTag = "f64"; return true;
        }
        // Schema typedef heuristic: every `*Key` typedef in 1.06 is a
        // 4-byte u32 (ItemKey, MissionKey, FactionKey, etc.). We gate
        // on metaSize so a future patch that introduces a wider key
        // type doesn't get misclassified silently.
        if (metaSize == 4 && typeName.EndsWith("Key", StringComparison.Ordinal))
        {
            typeTag = "u32";
            return true;
        }
        return false;
    }

    /// <summary>
    /// Split <paramref name="formatted"/> ("123 &lt;u32&gt;") into the raw
    /// stringified value ("123") and the lowercase type tag ("u32"). Returns
    /// false when the input doesn't carry the expected trailing tag.
    /// </summary>
    public static bool TryParse(string formatted, out string rawText, out string typeTag)
    {
        rawText = string.Empty;
        typeTag = string.Empty;
        if (string.IsNullOrEmpty(formatted))
        {
            return false;
        }
        var lastSpace = formatted.LastIndexOf(' ');
        if (lastSpace < 0 || lastSpace + 1 >= formatted.Length)
        {
            return false;
        }
        var tail = formatted.AsSpan(lastSpace + 1);
        if (tail.Length < 3 || tail[0] != '<' || tail[^1] != '>')
        {
            return false;
        }
        typeTag = tail[1..^1].ToString();
        rawText = formatted[..lastSpace];
        return true;
    }

    /// <summary>
    /// Encode <paramref name="rawText"/> as a little-endian byte buffer for
    /// type <paramref name="typeTag"/>. On parse failure returns <c>false</c>
    /// and writes a user-friendly error into <paramref name="error"/>; on
    /// success populates <paramref name="bytes"/> and clears
    /// <paramref name="error"/>.
    /// </summary>
    /// <remarks>
    /// Numeric parsing is culture-invariant so the editor behaves identically
    /// regardless of the user's locale (e.g. "1,5" vs "1.5" for f32).
    /// </remarks>
    public static bool TryEncode(string typeTag, string rawText, out byte[] bytes, out string error)
    {
        bytes = [];
        error = string.Empty;
        var trimmed = rawText?.Trim() ?? string.Empty;
        var ci = CultureInfo.InvariantCulture;

        switch (typeTag)
        {
            case "bool":
                if (bool.TryParse(trimmed, out var b))
                {
                    bytes = [(byte)(b ? 1 : 0)];
                    return true;
                }
                error = $"Expected 'true' or 'false' for bool, got '{trimmed}'.";
                return false;

            case "u8":
                if (byte.TryParse(trimmed, NumberStyles.Integer, ci, out var u8))
                {
                    bytes = [u8];
                    return true;
                }
                error = $"Value '{trimmed}' is out of range for u8 (0..255).";
                return false;

            case "u16":
                if (ushort.TryParse(trimmed, NumberStyles.Integer, ci, out var u16))
                {
                    bytes = BitConverter.GetBytes(u16);
                    return true;
                }
                error = $"Value '{trimmed}' is out of range for u16 (0..65535).";
                return false;

            case "u32":
                if (uint.TryParse(trimmed, NumberStyles.Integer, ci, out var u32))
                {
                    bytes = BitConverter.GetBytes(u32);
                    return true;
                }
                error = $"Value '{trimmed}' is out of range for u32 (0..4294967295).";
                return false;

            case "u64":
                if (ulong.TryParse(trimmed, NumberStyles.Integer, ci, out var u64))
                {
                    bytes = BitConverter.GetBytes(u64);
                    return true;
                }
                error = $"Value '{trimmed}' is out of range for u64.";
                return false;

            case "i8":
                if (sbyte.TryParse(trimmed, NumberStyles.Integer, ci, out var i8))
                {
                    bytes = [unchecked((byte)i8)];
                    return true;
                }
                error = $"Value '{trimmed}' is out of range for i8 (-128..127).";
                return false;

            case "i16":
                if (short.TryParse(trimmed, NumberStyles.Integer, ci, out var i16))
                {
                    bytes = BitConverter.GetBytes(i16);
                    return true;
                }
                error = $"Value '{trimmed}' is out of range for i16 (-32768..32767).";
                return false;

            case "i32":
                if (int.TryParse(trimmed, NumberStyles.Integer, ci, out var i32))
                {
                    bytes = BitConverter.GetBytes(i32);
                    return true;
                }
                error = $"Value '{trimmed}' is out of range for i32.";
                return false;

            case "i64":
                if (long.TryParse(trimmed, NumberStyles.Integer, ci, out var i64))
                {
                    bytes = BitConverter.GetBytes(i64);
                    return true;
                }
                error = $"Value '{trimmed}' is out of range for i64.";
                return false;

            case "f32":
                if (float.TryParse(trimmed, NumberStyles.Float, ci, out var f32))
                {
                    bytes = BitConverter.GetBytes(f32);
                    return true;
                }
                error = $"Value '{trimmed}' is not a valid f32.";
                return false;

            case "f64":
                if (double.TryParse(trimmed, NumberStyles.Float, ci, out var f64))
                {
                    bytes = BitConverter.GetBytes(f64);
                    return true;
                }
                error = $"Value '{trimmed}' is not a valid f64.";
                return false;

            default:
                error = $"Type '{typeTag}' is not editable through the scalar surface.";
                return false;
        }
    }
}
