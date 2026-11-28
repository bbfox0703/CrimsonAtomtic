using System.Buffers.Binary;
using CrimsonAtomtic.RustInterop;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// What a failed mutation inside a deferred batch leaves behind. crimson-rs
/// has no savepoint, so the ISaveLoader contract is: everything before the
/// failure stays applied (even the earlier ops of the same multi-op call,
/// which outside a batch is all-or-nothing), and only throwing out of
/// <see cref="ISaveLoader.RunDeferred"/> rolls the whole batch back.
/// </summary>
public sealed class DeferredFailureTests
{
    private static byte[] U32(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }

    private static List<ScalarBatchOp> ValidThenBad() =>
    [
        new(0, [], 0, U32(0xDEADBEEFu)),
        new(0, [], 0, [0, 0, 0]), // u32 field, 3 bytes: LENGTH_MISMATCH
    ];

    [Fact]
    public void FailedBatchOp_InsideDeferred_KeepsEverythingBeforeTheFailure()
    {
        var path = LiveSaves.Newest();
        if (path is null) return;
        using var loader = new NativeSaveLoader();
        loader.Load(path);
        var original = loader.LoadBlockDetails(path, 0).Fields[0].Value;

        loader.BeginDeferredRedecode();
        try
        {
            loader.SetScalarField(0, 0, U32(0x01020304u));
            Assert.NotEqual(original, loader.LoadBlockDetails(path, 0).Fields[0].Value);

            var ex = Assert.Throws<CrimsonSaveException>(() => loader.SetScalarFieldsBatch(ValidThenBad()));
            Assert.Equal(1, ex.FailedOpIndex);

            // Op 0 of the failed call is applied, unlike outside a batch.
            Assert.Equal($"{0xDEADBEEFu} <u32>", loader.LoadBlockDetails(path, 0).Fields[0].Value);
            Assert.True(loader.IsDeferredRedecodeOpen());
        }
        finally
        {
            loader.AbortDeferredRedecode();
        }
        Assert.Equal(original, loader.LoadBlockDetails(path, 0).Fields[0].Value);
    }

    [Fact]
    public void RunDeferred_BodyRethrowingAFailure_RollsBackEarlierMutations()
    {
        var path = LiveSaves.Newest();
        if (path is null) return;
        using var loader = new NativeSaveLoader();
        loader.Load(path);
        var original = loader.LoadBlockDetails(path, 0).Fields[0].Value;

        Assert.Throws<CrimsonSaveException>(() => loader.RunDeferred(() =>
        {
            loader.SetScalarField(0, 0, U32(0x01020304u));
            loader.SetScalarFieldsBatch(ValidThenBad());
        }));

        Assert.False(loader.IsDeferredRedecodeOpen());
        Assert.Equal(original, loader.LoadBlockDetails(path, 0).Fields[0].Value);
    }

    [Fact]
    public void RunDeferred_BodySwallowingAFailure_CommitsThePartlyAppliedTree()
    {
        // The documented hazard: catching inside the body commits what ran.
        var path = LiveSaves.Newest();
        if (path is null) return;
        using var loader = new NativeSaveLoader();
        loader.Load(path);
        var original = loader.LoadBlockDetails(path, 0).Fields[0].Value;

        loader.RunDeferred(() =>
        {
            loader.SetScalarField(0, 0, U32(0x01020304u));
            try
            {
                loader.SetScalarFieldsBatch(ValidThenBad());
            }
            catch (CrimsonSaveException)
            {
                // Swallowed on purpose.
            }
        });

        Assert.False(loader.IsDeferredRedecodeOpen());
        Assert.NotEqual(original, loader.LoadBlockDetails(path, 0).Fields[0].Value);
    }
}
