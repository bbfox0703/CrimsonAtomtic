using System.Runtime.InteropServices;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// One descent step in a path passed to
/// <see cref="ISaveLoader.SetScalarField(int, System.ReadOnlySpan{PathStep}, int, System.ReadOnlySpan{byte})"/>.
/// Mirrors <c>CrimsonPathStep</c> on the Rust side (matched layout so the
/// span passes straight across the FFI without per-step marshalling).
/// </summary>
/// <remarks>
/// <para>
/// Each step says: "from the current block, look up <see cref="FieldIndex"/>;
/// if that field is an <c>ObjectLocator</c> with a resolved inline child,
/// descend into the child (<see cref="ElementIndex"/> is ignored); if it's
/// an <c>ObjectList</c>, descend into element <see cref="ElementIndex"/>".
/// Anything else fails with <c>NOT_NAVIGABLE (-15)</c>.
/// </para>
/// <para>
/// Indices are <see cref="uint"/> to match the Rust side and keep the
/// FFI struct layout identical. Use 0 for <see cref="ElementIndex"/> on
/// locator steps as a convention.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct PathStep(uint FieldIndex, uint ElementIndex);
