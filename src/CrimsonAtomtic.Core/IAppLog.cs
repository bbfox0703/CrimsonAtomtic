namespace CrimsonAtomtic.Core;

/// <summary>
/// The application log (Rule 6): one line per event, grouped by category
/// (<c>save</c>, <c>gamedata</c>, <c>errors</c>, <c>app</c>). Writing never
/// throws; a log that cannot be written is dropped, not reported.
/// </summary>
public interface IAppLog
{
    void Write(string category, string message);
}
