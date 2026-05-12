namespace CrimsonAtomtic.Core;

/// <summary>
/// Ensures only one instance of the UI runs at a time. The implementation
/// is platform-specific (Windows uses a named Mutex; other platforms use
/// a lock file or equivalent) and lives outside this project.
/// </summary>
public interface ISingleInstanceGuard : IDisposable
{
    /// <summary>
    /// Try to acquire the single-instance token. Returns <c>true</c> if
    /// this process now owns it, <c>false</c> if another instance already
    /// holds it (in which case the caller should exit).
    /// </summary>
    bool TryAcquire();
}
