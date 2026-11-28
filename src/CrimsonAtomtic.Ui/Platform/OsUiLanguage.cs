using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CrimsonAtomtic.Core;

namespace CrimsonAtomtic.Ui.Platform;

/// <summary>The <see cref="IOsUiLanguage"/> for the running OS.</summary>
public static class OsUiLanguage
{
    public static IOsUiLanguage ForCurrentOs() =>
        OperatingSystem.IsWindows() ? new WindowsOsUiLanguage() : new EnvironmentOsUiLanguage();
}

/// <summary>
/// Windows: <c>GetUserDefaultUILanguage</c>. The build runs with
/// <c>InvariantGlobalization</c>, so <c>CultureInfo</c> reports no culture
/// name at all and the Win32 call is the only reliable signal.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsOsUiLanguage : IOsUiLanguage
{
    public string? PreferredLanguageTag()
    {
        ushort lcid;
        try
        {
            lcid = GetUserDefaultUILanguage();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        if (lcid == 0)
        {
            return null;
        }
        // LCID: low 10 bits primary language, next 6 bits sublanguage.
        var primary = lcid & 0x3FF;
        var sub = (lcid >> 10) & 0x3F;
        return primary switch
        {
            0x11 => "ja",
            0x04 => sub is 1 or 3 or 5 ? "zh-TW" : "zh-CN",
            0x09 => "en",
            _ => "und",
        };
    }

    // DllImport, not LibraryImport: LibraryImport needs AllowUnsafeBlocks on
    // the project, and a parameterless ushort return needs no marshalling.
    [DllImport("kernel32.dll", EntryPoint = "GetUserDefaultUILanguage", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern ushort GetUserDefaultUILanguage();
}

/// <summary>
/// Linux and macOS: the POSIX locale variables, most specific first
/// (<c>LC_ALL</c>, <c>LC_MESSAGES</c>, <c>LANG</c>), e.g. <c>ja_JP.UTF-8</c>.
/// </summary>
public sealed class EnvironmentOsUiLanguage : IOsUiLanguage
{
    public string? PreferredLanguageTag()
    {
        foreach (var name in new[] { "LC_ALL", "LC_MESSAGES", "LANG" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value) || value is "C" or "POSIX")
            {
                continue;
            }
            var locale = value.Split('.', '@')[0];
            return locale.Replace('_', '-');
        }
        return null;
    }
}
