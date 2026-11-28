using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CrimsonAtomtic.RustInterop;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Size and field offsets of every struct crossing the C ABI by value or
/// through a pointer, against the <c>#[repr(C)]</c> struct of the same name
/// in <c>vendor/crimson-rs/src/c_abi/</c> (x64). A drifted layout makes
/// the native side read the wrong bytes without any error, so a mismatch
/// here is the only signal.
/// </summary>
public sealed class InteropLayoutTests
{
    private static int Off<T>(string field) => (int)Marshal.OffsetOf<T>(field);

    // Record structs keep positional members in compiler-named backing fields.
    private static string Backing(string property) => $"<{property}>k__BackingField";

    [Fact]
    public void InventoryItemRecord_MatchesCrimsonInventoryItemRecord()
    {
        Assert.Equal(48, Unsafe.SizeOf<InventoryItemRecord>());
        Assert.Equal(0, Off<InventoryItemRecord>(nameof(InventoryItemRecord.BlockIndex)));
        Assert.Equal(4, Off<InventoryItemRecord>(nameof(InventoryItemRecord.InventoryElementIndex)));
        Assert.Equal(8, Off<InventoryItemRecord>(nameof(InventoryItemRecord.ItemElementIndex)));
        Assert.Equal(12, Off<InventoryItemRecord>(nameof(InventoryItemRecord.InventoryKey)));
        Assert.Equal(16, Off<InventoryItemRecord>(nameof(InventoryItemRecord.ItemKey)));
        Assert.Equal(20, Off<InventoryItemRecord>(nameof(InventoryItemRecord.TransferredItemKey)));
        Assert.Equal(24, Off<InventoryItemRecord>(nameof(InventoryItemRecord.SlotNo)));
        Assert.Equal(28, Off<InventoryItemRecord>(nameof(InventoryItemRecord.Flags)));
        Assert.Equal(32, Off<InventoryItemRecord>(nameof(InventoryItemRecord.ItemNo)));
        Assert.Equal(40, Off<InventoryItemRecord>(nameof(InventoryItemRecord.StackCount)));
    }

    [Fact]
    public void CharacterRefRecord_MatchesCrimsonCharacterRefRecord()
    {
        Assert.Equal(16, Unsafe.SizeOf<CharacterRefRecord>());
        Assert.Equal(0, Off<CharacterRefRecord>(nameof(CharacterRefRecord.BlockIndex)));
        Assert.Equal(4, Off<CharacterRefRecord>(nameof(CharacterRefRecord.CharacterKey)));
        Assert.Equal(8, Off<CharacterRefRecord>(nameof(CharacterRefRecord.ClassIndex)));
        Assert.Equal(12, Off<CharacterRefRecord>(nameof(CharacterRefRecord.Reserved0)));
    }

    [Fact]
    public void PazFileEntry_MatchesCrimsonPazFileEntry()
    {
        // name: [u8; 256] first, then four u32s.
        Assert.Equal(272, Unsafe.SizeOf<PazFileEntry>());
        Assert.Equal(256, Off<PazFileEntry>(nameof(PazFileEntry.CompressedSize)));
        Assert.Equal(260, Off<PazFileEntry>(nameof(PazFileEntry.UncompressedSize)));
        Assert.Equal(264, Off<PazFileEntry>("_isPartial"));
        Assert.Equal(268, Off<PazFileEntry>("_nameTruncated"));
    }

    [Fact]
    public void PathStep_MatchesCrimsonPathStep()
    {
        Assert.Equal(8, Unsafe.SizeOf<PathStep>());
        Assert.Equal(0, Off<PathStep>(Backing(nameof(PathStep.FieldIndex))));
        Assert.Equal(4, Off<PathStep>(Backing(nameof(PathStep.ElementIndex))));
    }

    [Fact]
    public void ScalarBatchOp_MatchesCrimsonScalarBatchOp()
    {
        Assert.Equal(40, Unsafe.SizeOf<NativeMethods.CrimsonScalarBatchOp>());
        Assert.Equal(0, Off<NativeMethods.CrimsonScalarBatchOp>("BlockIdx"));
        Assert.Equal(4, Off<NativeMethods.CrimsonScalarBatchOp>("FieldIdx"));
        Assert.Equal(8, Off<NativeMethods.CrimsonScalarBatchOp>("Path"));
        Assert.Equal(16, Off<NativeMethods.CrimsonScalarBatchOp>("PathLen"));
        Assert.Equal(24, Off<NativeMethods.CrimsonScalarBatchOp>("Bytes"));
        Assert.Equal(32, Off<NativeMethods.CrimsonScalarBatchOp>("BytesLen"));
    }

    [Fact]
    public void ScalarPresentBatchOp_MatchesCrimsonScalarPresentBatchOp()
    {
        Assert.Equal(48, Unsafe.SizeOf<NativeMethods.CrimsonScalarPresentBatchOp>());
        Assert.Equal(0, Off<NativeMethods.CrimsonScalarPresentBatchOp>("BlockIdx"));
        Assert.Equal(4, Off<NativeMethods.CrimsonScalarPresentBatchOp>("FieldIdx"));
        Assert.Equal(8, Off<NativeMethods.CrimsonScalarPresentBatchOp>("Path"));
        Assert.Equal(16, Off<NativeMethods.CrimsonScalarPresentBatchOp>("PathLen"));
        Assert.Equal(24, Off<NativeMethods.CrimsonScalarPresentBatchOp>("MakePresent"));
        Assert.Equal(32, Off<NativeMethods.CrimsonScalarPresentBatchOp>("Bytes"));
        Assert.Equal(40, Off<NativeMethods.CrimsonScalarPresentBatchOp>("BytesLen"));
    }

    [Fact]
    public void ListRemoveBatchOp_MatchesCrimsonListRemoveBatchOp()
    {
        Assert.Equal(32, Unsafe.SizeOf<NativeMethods.CrimsonListRemoveBatchOp>());
        Assert.Equal(0, Off<NativeMethods.CrimsonListRemoveBatchOp>("BlockIdx"));
        Assert.Equal(4, Off<NativeMethods.CrimsonListRemoveBatchOp>("FieldIdx"));
        Assert.Equal(8, Off<NativeMethods.CrimsonListRemoveBatchOp>("Path"));
        Assert.Equal(16, Off<NativeMethods.CrimsonListRemoveBatchOp>("PathLen"));
        Assert.Equal(24, Off<NativeMethods.CrimsonListRemoveBatchOp>("ElementIdx"));
    }
}
