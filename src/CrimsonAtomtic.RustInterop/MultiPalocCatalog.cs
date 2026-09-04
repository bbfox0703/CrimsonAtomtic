namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// One language's localization table when the install splits it across
/// several PALOC files.
///
/// <para>
/// Through 2.00 a language was a single
/// <c>localizationstring_&lt;lang&gt;.paloc</c> blob. 2.01 split it into one
/// file per namespace under a per-language directory. The container is a
/// flat entry list and the namespace is already encoded in every entry's
/// key, so the split is presentational: querying the parts in load order is
/// equivalent to querying the pre-2.01 whole.
/// </para>
///
/// <para>
/// Composes <see cref="IPalocCatalog"/> rather than merging bytes, which
/// keeps the merge out of the parser — crimson-rs reads either layout
/// unchanged — and costs one extra dictionary probe per miss.
/// </para>
/// </summary>
public sealed class MultiPalocCatalog : IPalocCatalog
{
    private readonly IPalocCatalog[] _parts;
    private bool _disposed;

    /// <summary>
    /// Take ownership of <paramref name="parts"/> in load order. Disposing
    /// this disposes every part.
    /// </summary>
    public MultiPalocCatalog(IEnumerable<IPalocCatalog> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        _parts = [.. parts];
        if (_parts.Length == 0)
        {
            throw new ArgumentException("At least one catalog is required.", nameof(parts));
        }
    }

    /// <summary>Number of PALOC files backing this language.</summary>
    public int PartCount => _parts.Length;

    /// <inheritdoc />
    public int EntryCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var total = 0;
            foreach (var part in _parts)
            {
                total += part.EntryCount;
            }
            return total;
        }
    }

    /// <inheritdoc />
    /// <remarks>First part holding the key wins. Namespaces don't overlap,
    /// so at most one part can answer in practice.</remarks>
    public string? Lookup(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var part in _parts)
        {
            if (part.Lookup(key) is { } hit)
            {
                return hit;
            }
        }
        return null;
    }

    /// <inheritdoc />
    /// <remarks>Indexes the parts' concatenation in load order, so a walk
    /// from 0 to <see cref="EntryCount"/> visits every entry exactly
    /// once.</remarks>
    public (string Key, string Value)? GetEntry(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (index < 0)
        {
            return null;
        }
        foreach (var part in _parts)
        {
            var count = part.EntryCount;
            if (index < count)
            {
                return part.GetEntry(index);
            }
            index -= count;
        }
        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (var part in _parts)
        {
            part.Dispose();
        }
    }
}
