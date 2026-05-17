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
    // Composite-scalar tags landed in vendor/crimson-rs commit 94c7a96
    // (typed F32x3 / F32x4 / U32x4 ScalarValue variants). The raw value
    // is bracketed and comma-separated; the LastIndexOf-space split
    // still puts the trailing "<tag>" cleanly on the tag side.
    [InlineData("[1.5, 2, -3.25] <f32x3>",          "[1.5, 2, -3.25]",          "f32x3")]
    [InlineData("[0.5, 0, 1, 0] <f32x4>",           "[0.5, 0, 1, 0]",           "f32x4")]
    [InlineData("[0x12345678, 0xdeadbeef, 0x00000001, 0xffffffff] <u32x4>",
                "[0x12345678, 0xdeadbeef, 0x00000001, 0xffffffff]", "u32x4")]
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

    // Composite-scalar SETTER coverage (2026-05-17, vendor 23a9e0d).
    // Replaces the prior "always rejected" stance — the bracketed
    // display format the read side emits is now round-trippable.
    // (IsTextEditable_CompositeScalarReturnsTrue below replaces the
    // old IsTextEditable_CompositeScalarReturnsFalse.)

    [Theory]
    [InlineData("f32x3", "[1.5, 2.0, -3.25]", new byte[] {
        0x00, 0x00, 0xC0, 0x3F,  // 1.5f
        0x00, 0x00, 0x00, 0x40,  // 2.0f
        0x00, 0x00, 0x50, 0xC0,  // -3.25f
    })]
    [InlineData("f32x3", "1.5, 2.0, -3.25", new byte[] {
        0x00, 0x00, 0xC0, 0x3F,
        0x00, 0x00, 0x00, 0x40,
        0x00, 0x00, 0x50, 0xC0,
    })]  // bare (no brackets) also accepted
    [InlineData("f32x4", "[0.5, 0, 1, 0]", new byte[] {
        0x00, 0x00, 0x00, 0x3F,  // 0.5f
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x80, 0x3F,  // 1.0f
        0x00, 0x00, 0x00, 0x00,
    })]
    public void TryEncode_CompositeFloatVec_RoundTripsLittleEndian(
        string tag, string input, byte[] expected)
    {
        Assert.True(ScalarFieldEditing.TryEncode(tag, input, out var bytes, out var err));
        Assert.Equal("", err);
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("u32x4", "[0x12345678, 0xdeadbeef, 1, 4294967295]", new byte[] {
        0x78, 0x56, 0x34, 0x12,
        0xEF, 0xBE, 0xAD, 0xDE,
        0x01, 0x00, 0x00, 0x00,
        0xFF, 0xFF, 0xFF, 0xFF,
    })]
    [InlineData("u32x4", "0,0,0,0", new byte[] {
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    })]
    public void TryEncode_CompositeUintVec_AcceptsHexAndDecimal(
        string tag, string input, byte[] expected)
    {
        Assert.True(ScalarFieldEditing.TryEncode(tag, input, out var bytes, out var err));
        Assert.Equal("", err);
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("f32x3", "1.5, 2.0", "Expected 3")]               // too few
    [InlineData("f32x3", "1, 2, 3, 4", "Expected 3")]              // too many
    [InlineData("f32x4", "[]", "empty list")]
    [InlineData("f32x3", "1, 2, abc", "not a valid f32")]
    [InlineData("u32x4", "1, 2, 3, -1", "not a valid u32 decimal")] // negative -> reject
    [InlineData("u32x4", "1, 2, 3, 0xZZ", "not a valid u32 hex literal")]
    public void TryEncode_CompositeVec_RejectsMalformedInput(string tag, string input, string errFragment)
    {
        Assert.False(ScalarFieldEditing.TryEncode(tag, input, out var bytes, out var err));
        Assert.Empty(bytes);
        Assert.Contains(errFragment, err);
    }

    [Theory]
    [InlineData("[1.5, 2, -3.25] <f32x3>")]
    [InlineData("[0.5, 0, 1, 0] <f32x4>")]
    [InlineData("[0x12345678, 0xdeadbeef, 0x00000001, 0xffffffff] <u32x4>")]
    public void IsTextEditable_CompositeScalarReturnsTrue(string value)
    {
        // Inverted from the 94c7a96 "always rejected" rule once the
        // typed setters landed (23a9e0d). The single textbox now
        // round-trips bracketed component lists.
        var row = MakeRow(kind: "fixed_prefix", value: value);
        Assert.True(ScalarFieldEditing.IsTextEditable(row));
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

    [Theory]
    [InlineData("bool",   1, "bool")]
    [InlineData("uint8",  1, "u8")]
    [InlineData("byte",   1, "u8")]
    [InlineData("uint16", 2, "u16")]
    [InlineData("uint32", 4, "u32")]
    [InlineData("uint64", 8, "u64")]
    [InlineData("int8",   1, "i8")]
    [InlineData("sbyte",  1, "i8")]
    [InlineData("int16",  2, "i16")]
    [InlineData("int32",  4, "i32")]
    [InlineData("int64",  8, "i64")]
    [InlineData("float",  4, "f32")]
    [InlineData("single", 4, "f32")]
    [InlineData("double", 8, "f64")]
    [InlineData("u32",    4, "u32")]
    [InlineData("f64",    8, "f64")]
    public void TryInferTypeTagFromSchema_PrimitiveTypeNamesResolve(
        string typeName, int metaSize, string expected)
    {
        Assert.True(ScalarFieldEditing.TryInferTypeTagFromSchema(typeName, metaSize, out var tag));
        Assert.Equal(expected, tag);
    }

    [Theory]
    [InlineData("ItemKey",      4)]
    [InlineData("MissionKey",   4)]
    [InlineData("QuestKey",     4)]
    [InlineData("StageKey",     4)]
    [InlineData("KnowledgeKey", 4)]
    [InlineData("FactionKey",   4)]
    [InlineData("CharacterKey", 4)]
    public void TryInferTypeTagFromSchema_KeyTypedefsResolveToU32(string typeName, int metaSize)
    {
        // Every `*Key` typedef in the 1.06 schema is a u32 under the hood.
        // The size gate keeps this from misclassifying a future patch that
        // introduces a wider key shape.
        Assert.True(ScalarFieldEditing.TryInferTypeTagFromSchema(typeName, metaSize, out var tag));
        Assert.Equal("u32", tag);
    }

    [Theory]
    [InlineData("MissionKey", 8)] // future-patch divergence; size-gated rejection
    [InlineData("",          4)]
    [InlineData("float3",    8)]  // size mismatch — float3 must be 12 bytes
    [InlineData("float4",   12)]  // size mismatch — float4 must be 16 bytes
    [InlineData("Quaternion", 8)] // size mismatch
    [InlineData("uint4",     8)]  // size mismatch
    public void TryInferTypeTagFromSchema_NonScalarOrUnknownReturnsFalse(string typeName, int metaSize)
    {
        Assert.False(ScalarFieldEditing.TryInferTypeTagFromSchema(typeName, metaSize, out var tag));
        Assert.Equal(string.Empty, tag);
    }

    [Theory]
    [InlineData("float3",         12, "f32x3")]
    [InlineData("float4",         16, "f32x4")]
    [InlineData("Quaternion",     16, "f32x4")]  // quaternion shares the 16B float4 shape
    [InlineData("quaternion",     16, "f32x4")]
    [InlineData("uint4",          16, "u32x4")]
    [InlineData("SceneObjectUuid", 16, "u32x4")] // 128-bit ID stored as uint4
    public void TryInferTypeTagFromSchema_CompositeTypeNamesResolve(
        string typeName, int metaSize, string expected)
    {
        Assert.True(ScalarFieldEditing.TryInferTypeTagFromSchema(typeName, metaSize, out var tag));
        Assert.Equal(expected, tag);
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
