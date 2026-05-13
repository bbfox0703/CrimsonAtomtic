using CrimsonAtomtic.SaveModel;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Pure-CPU tests for <see cref="ScalarFieldEditing"/>. No game data needed —
/// these run on every machine, no skip path.
/// </summary>
public sealed class ScalarFieldEditingTests
{
    [Theory]
    [InlineData("32492971 <u32>",  "32492971", "u32")]
    [InlineData("true <bool>",     "true",     "bool")]
    [InlineData("1.5 <f32>",       "1.5",      "f32")]
    [InlineData("-7 <i8>",         "-7",       "i8")]
    [InlineData("100 bytes <bytes>", "100 bytes", "bytes")]
    public void TryParse_FormattedValue_SplitsRawAndTag(string formatted, string expectedRaw, string expectedTag)
    {
        Assert.True(ScalarFieldEditing.TryParse(formatted, out var raw, out var tag));
        Assert.Equal(expectedRaw, raw);
        Assert.Equal(expectedTag, tag);
    }

    [Theory]
    [InlineData("")]
    [InlineData("(absent)")]
    [InlineData("<unknown>")]
    [InlineData("-> InventoryElementSaveData (offset 8128)")]
    public void TryParse_NonScalarValue_ReturnsFalse(string formatted)
    {
        Assert.False(ScalarFieldEditing.TryParse(formatted, out _, out _));
    }

    [Fact]
    public void TryEncode_U32_RoundTripsLittleEndian()
    {
        Assert.True(ScalarFieldEditing.TryEncode("u32", "32492971", out var bytes, out var err));
        Assert.Equal("", err);
        Assert.Equal(new byte[] { 0xAB, 0xCD, 0xEF, 0x01 }, bytes);
    }

    [Fact]
    public void TryEncode_Bool_AcceptsTrueAndFalse()
    {
        Assert.True(ScalarFieldEditing.TryEncode("bool", "true",  out var t, out _));
        Assert.Equal(new byte[] { 1 }, t);

        Assert.True(ScalarFieldEditing.TryEncode("bool", " False ", out var f, out _));
        Assert.Equal(new byte[] { 0 }, f);
    }

    [Fact]
    public void TryEncode_Bool_RejectsNumericInput()
    {
        Assert.False(ScalarFieldEditing.TryEncode("bool", "1", out var bytes, out var err));
        Assert.Empty(bytes);
        Assert.Contains("bool", err);
    }

    [Fact]
    public void TryEncode_NegativeAndSignedIntegers()
    {
        Assert.True(ScalarFieldEditing.TryEncode("i8",  "-1",      out var i8,  out _));
        Assert.Equal(new byte[] { 0xFF }, i8);

        Assert.True(ScalarFieldEditing.TryEncode("i16", "-32768",  out var i16, out _));
        Assert.Equal(new byte[] { 0x00, 0x80 }, i16);

        Assert.True(ScalarFieldEditing.TryEncode("i32", "-1",      out var i32, out _));
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, i32);

        Assert.True(ScalarFieldEditing.TryEncode("i64", "-1",      out var i64, out _));
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, i64);
    }

    [Fact]
    public void TryEncode_FloatsAreCultureInvariant()
    {
        Assert.True(ScalarFieldEditing.TryEncode("f32", "1.5", out var f32, out _));
        Assert.Equal(4, f32.Length);
        Assert.Equal(1.5f, BitConverter.ToSingle(f32, 0));

        Assert.True(ScalarFieldEditing.TryEncode("f64", "-3.25", out var f64, out _));
        Assert.Equal(8, f64.Length);
        Assert.Equal(-3.25, BitConverter.ToDouble(f64, 0));
    }

    [Theory]
    [InlineData("u8",  "256")]
    [InlineData("u16", "65536")]
    [InlineData("u32", "-1")]
    [InlineData("i8",  "200")]
    [InlineData("i16", "40000")]
    [InlineData("f32", "abc")]
    [InlineData("f64", "1.2.3")]
    public void TryEncode_OutOfRangeOrMalformed_FailsCleanly(string tag, string text)
    {
        Assert.False(ScalarFieldEditing.TryEncode(tag, text, out var bytes, out var err));
        Assert.Empty(bytes);
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Fact]
    public void TryEncode_UnsupportedTag_ReturnsFalse()
    {
        Assert.False(ScalarFieldEditing.TryEncode("bytes", "anything", out _, out var err));
        Assert.Contains("bytes", err);
    }

    [Fact]
    public void IsTextEditable_ScalarOfSupportedTypeReturnsTrue()
    {
        var row = MakeRow(kind: "fixed_suffix", value: "32492971 <u32>");
        Assert.True(ScalarFieldEditing.IsTextEditable(row));
    }

    [Fact]
    public void IsTextEditable_BytesScalarReturnsFalse()
    {
        var row = MakeRow(kind: "fixed_prefix", value: "100 bytes <bytes>");
        Assert.False(ScalarFieldEditing.IsTextEditable(row));
    }

    [Fact]
    public void IsTextEditable_NonScalarKindReturnsFalse()
    {
        var locator = MakeRow(kind: "object_locator", value: "-> Foo (offset 8128)");
        Assert.False(ScalarFieldEditing.IsTextEditable(locator));

        var list = MakeRow(kind: "object_list", value: "[40 elements, variant=1]");
        Assert.False(ScalarFieldEditing.IsTextEditable(list));

        var absent = MakeRow(kind: "absent", value: "(absent)");
        Assert.False(ScalarFieldEditing.IsTextEditable(absent));
    }

    private static DecodedFieldRow MakeRow(string kind, string value) => new(
        FieldIndex: 0,
        Name: "_characterKey",
        TypeName: "u32",
        MetaKind: 0,
        MetaSize: 4,
        MetaAux: 0,
        Present: true,
        Kind: kind,
        Value: value,
        Start: 0,
        End: 4,
        Note: string.Empty,
        Child: null,
        Elements: null);
}
