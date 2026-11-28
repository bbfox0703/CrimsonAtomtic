using CrimsonAtomtic.RustInterop;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Tells a tool window whether the save changed since it built its rows.
/// Rows address items by element index, and another window's insert or
/// remove shifts those indices, so a write against a stale snapshot lands
/// on a different item. The guard compares the loader's mutation version,
/// which only moves forward, with the version the rows were built at; the
/// owner calls <see cref="Resync"/> after its own writes so they do not
/// read as someone else's.
/// </summary>
public sealed class SnapshotGuard
{
    private readonly ISaveLoader _loader;
    private ulong _version;

    public SnapshotGuard(ISaveLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
        Resync();
    }

    /// <summary>
    /// True when the save was mutated after the last <see cref="Resync"/>
    /// or <see cref="Capture"/>. False when no save is loaded.
    /// </summary>
    public bool IsStale
    {
        get
        {
            try
            {
                return _loader.GetMutationVersion() != _version;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>Take the loader's current version as the snapshot's.</summary>
    public void Resync()
    {
        try
        {
            _version = _loader.GetMutationVersion();
        }
        catch (InvalidOperationException)
        {
            _version = 0;
        }
    }

    /// <summary>
    /// Take <paramref name="version"/>, a version a read returned with its
    /// rows (for example <see cref="ISaveLoader.ListAllItems"/>), as the
    /// snapshot's.
    /// </summary>
    public void Capture(ulong version) => _version = version;
}
