namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// Typed convenience setters over
/// <see cref="ISaveLoader.SetScalarField(int, int, ReadOnlySpan{byte})"/>.
/// Each wraps a <see cref="BitConverter"/> call so the caller doesn't have
/// to fiddle with little-endian byte buffers. Rust validates the byte
/// count against the field's recorded type; a mismatch surfaces as a
/// <see cref="CrimsonSaveException"/> with code <c>LENGTH_MISMATCH (-13)</c>.
/// </summary>
public static class SaveLoaderScalarExtensions
{
    public static void SetScalarBool(this ISaveLoader loader, int blockIndex, int fieldIndex, bool value) =>
        loader.SetScalarBool(blockIndex, ReadOnlySpan<PathStep>.Empty, fieldIndex, value);

    public static void SetScalarBool(this ISaveLoader loader, int blockIndex, ReadOnlySpan<PathStep> path, int fieldIndex, bool value)
    {
        Span<byte> bytes = stackalloc byte[1];
        bytes[0] = value ? (byte)1 : (byte)0;
        loader.SetScalarField(blockIndex, path, fieldIndex, bytes);
    }

    public static void SetScalarUInt8(this ISaveLoader loader, int blockIndex, int fieldIndex, byte value) =>
        loader.SetScalarUInt8(blockIndex, ReadOnlySpan<PathStep>.Empty, fieldIndex, value);

    public static void SetScalarUInt8(this ISaveLoader loader, int blockIndex, ReadOnlySpan<PathStep> path, int fieldIndex, byte value)
    {
        Span<byte> bytes = stackalloc byte[1];
        bytes[0] = value;
        loader.SetScalarField(blockIndex, path, fieldIndex, bytes);
    }

    public static void SetScalarInt8(this ISaveLoader loader, int blockIndex, int fieldIndex, sbyte value) =>
        loader.SetScalarInt8(blockIndex, ReadOnlySpan<PathStep>.Empty, fieldIndex, value);

    public static void SetScalarInt8(this ISaveLoader loader, int blockIndex, ReadOnlySpan<PathStep> path, int fieldIndex, sbyte value)
    {
        Span<byte> bytes = stackalloc byte[1];
        bytes[0] = unchecked((byte)value);
        loader.SetScalarField(blockIndex, path, fieldIndex, bytes);
    }

    public static void SetScalarUInt16(this ISaveLoader loader, int blockIndex, int fieldIndex, ushort value) =>
        loader.SetScalarUInt16(blockIndex, ReadOnlySpan<PathStep>.Empty, fieldIndex, value);

    public static void SetScalarUInt16(this ISaveLoader loader, int blockIndex, ReadOnlySpan<PathStep> path, int fieldIndex, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        loader.SetScalarField(blockIndex, path, fieldIndex, bytes);
    }

    public static void SetScalarInt16(this ISaveLoader loader, int blockIndex, int fieldIndex, short value) =>
        loader.SetScalarInt16(blockIndex, ReadOnlySpan<PathStep>.Empty, fieldIndex, value);

    public static void SetScalarInt16(this ISaveLoader loader, int blockIndex, ReadOnlySpan<PathStep> path, int fieldIndex, short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        loader.SetScalarField(blockIndex, path, fieldIndex, bytes);
    }

    public static void SetScalarUInt32(this ISaveLoader loader, int blockIndex, int fieldIndex, uint value) =>
        loader.SetScalarUInt32(blockIndex, ReadOnlySpan<PathStep>.Empty, fieldIndex, value);

    public static void SetScalarUInt32(this ISaveLoader loader, int blockIndex, ReadOnlySpan<PathStep> path, int fieldIndex, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        loader.SetScalarField(blockIndex, path, fieldIndex, bytes);
    }

    public static void SetScalarInt32(this ISaveLoader loader, int blockIndex, int fieldIndex, int value) =>
        loader.SetScalarInt32(blockIndex, ReadOnlySpan<PathStep>.Empty, fieldIndex, value);

    public static void SetScalarInt32(this ISaveLoader loader, int blockIndex, ReadOnlySpan<PathStep> path, int fieldIndex, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        loader.SetScalarField(blockIndex, path, fieldIndex, bytes);
    }

    public static void SetScalarUInt64(this ISaveLoader loader, int blockIndex, int fieldIndex, ulong value) =>
        loader.SetScalarUInt64(blockIndex, ReadOnlySpan<PathStep>.Empty, fieldIndex, value);

    public static void SetScalarUInt64(this ISaveLoader loader, int blockIndex, ReadOnlySpan<PathStep> path, int fieldIndex, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        loader.SetScalarField(blockIndex, path, fieldIndex, bytes);
    }

    public static void SetScalarInt64(this ISaveLoader loader, int blockIndex, int fieldIndex, long value) =>
        loader.SetScalarInt64(blockIndex, ReadOnlySpan<PathStep>.Empty, fieldIndex, value);

    public static void SetScalarInt64(this ISaveLoader loader, int blockIndex, ReadOnlySpan<PathStep> path, int fieldIndex, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        loader.SetScalarField(blockIndex, path, fieldIndex, bytes);
    }

    public static void SetScalarSingle(this ISaveLoader loader, int blockIndex, int fieldIndex, float value) =>
        loader.SetScalarSingle(blockIndex, ReadOnlySpan<PathStep>.Empty, fieldIndex, value);

    public static void SetScalarSingle(this ISaveLoader loader, int blockIndex, ReadOnlySpan<PathStep> path, int fieldIndex, float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        loader.SetScalarField(blockIndex, path, fieldIndex, bytes);
    }

    public static void SetScalarDouble(this ISaveLoader loader, int blockIndex, int fieldIndex, double value) =>
        loader.SetScalarDouble(blockIndex, ReadOnlySpan<PathStep>.Empty, fieldIndex, value);

    public static void SetScalarDouble(this ISaveLoader loader, int blockIndex, ReadOnlySpan<PathStep> path, int fieldIndex, double value)
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
        loader.SetScalarField(blockIndex, path, fieldIndex, bytes);
    }
}
