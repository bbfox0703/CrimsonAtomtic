using CrimsonAtomtic.SaveModel;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// Loads a Crimson Desert save file into a <see cref="SaveSummary"/>.
/// The production implementation will call into <c>crimson_rs.dll</c>
/// via P/Invoke; the placeholder implementation in this project returns
/// canned data so the UI can be built and tested before the C ABI lands.
/// </summary>
public interface ISaveLoader
{
    /// <summary>
    /// Read <paramref name="savePath"/> from disk, parse it, and return a
    /// summary. Throws on malformed input; never returns <c>null</c>.
    /// </summary>
    SaveSummary Load(string savePath, CancellationToken cancellationToken = default);
}
