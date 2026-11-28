using System.Globalization;
using CrimsonAtomtic.SaveModel;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>Read helpers over decoded save fields, shared by the save-editing services.</summary>
internal static class SaveFieldValues
{
    /// <summary>
    /// The unsigned value of a present <c>u8</c>..<c>u64</c> scalar field
    /// (<c>"123 &lt;u32&gt;"</c>), or null when absent or of another type.
    /// </summary>
    public static ulong? TryU64(DecodedFieldRow f) =>
        f.Present
        && ScalarFieldEditing.TryParse(f.Value, out var raw, out var tag)
        && tag is "u8" or "u16" or "u32" or "u64"
        && ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    /// <summary>
    /// <paramref name="value"/> encoded in the width and type of
    /// <paramref name="element"/>'s field <paramref name="fieldIndex"/>, read
    /// from its type tag. Throws <see cref="InvalidOperationException"/> when
    /// the field has no scalar tag or the value does not fit it.
    /// </summary>
    public static byte[] EncodeLike(BlockDetails element, int fieldIndex, ulong value)
    {
        var field = element.Fields[fieldIndex];
        if (!ScalarFieldEditing.TryParse(field.Value, out _, out var tag))
        {
            throw new InvalidOperationException($"{field.Name} has no scalar value to take its type from.");
        }
        if (!ScalarFieldEditing.TryEncode(tag, value.ToString(CultureInfo.InvariantCulture),
                out var bytes, out var error))
        {
            throw new InvalidOperationException($"{field.Name} <{tag}>: {error}");
        }
        return bytes;
    }

    /// <summary>The field named <paramref name="name"/>, or null.</summary>
    public static DecodedFieldRow? Field(BlockDetails block, string name) =>
        block.Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));
}
