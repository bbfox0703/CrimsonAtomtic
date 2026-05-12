using CrimsonAtomtic.SaveModel;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// Loads a Crimson Desert save file into a <see cref="SaveSummary"/>.
/// The production implementation, <see cref="NativeSaveLoader"/>, P/Invokes
/// into <c>crimson_rs.dll</c> (built from <c>vendor/crimson-rs</c> with the
/// <c>c_abi</c> Cargo feature).
/// </summary>
public interface ISaveLoader
{
    /// <summary>
    /// Read <paramref name="savePath"/> from disk, parse it, and return a
    /// summary. Throws on malformed input; never returns <c>null</c>.
    /// </summary>
    SaveSummary Load(string savePath, CancellationToken cancellationToken = default);
}
