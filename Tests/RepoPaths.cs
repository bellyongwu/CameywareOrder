using System.IO;
using Path = System.IO.Path;

namespace CameywareOrder.Tests;

/// <summary>
/// Where the repository is, found from where the harness is RUNNING rather than hard-coded.
/// </summary>
/// <remarks>
/// The harnesses used to open with <c>Environment.CurrentDirectory = @"D:\Projects\..."</c>, which is
/// fine on one machine and useless on any other — and this project has already been moved between
/// drives once. Walking up from the assembly finds it wherever the repository is checked out, and
/// fails loudly if it cannot, rather than silently running against a half-populated
/// <c>Settings/System</c> and reporting the fallbacks as results.
///
/// Linked into each harness by the csproj rather than duplicated, so there is one definition of
/// "where does the shipped configuration live" for all of them.
/// </remarks>
internal static class RepoPaths
{
    /// <summary>
    /// The repository root — the folder holding <c>CameywareOrder.csproj</c>.
    /// </summary>
    public static string Root { get; } = FindRoot();

    /// <summary>
    /// Points the process at the repository, so <c>SystemSettingsPaths</c> finds the real language
    /// files and defaults. Call this FIRST in a harness, before anything reads configuration.
    /// </summary>
    /// <remarks>
    /// The application probes its own base directory and then the working directory. A harness's
    /// output folder carries a partial copy of <c>Settings/</c> at best, and a partial copy is worse
    /// than none: the probe succeeds on the folder and every missing file then reads as absent, which
    /// each loader answers by degrading silently to a built-in fallback. That is how one run came to
    /// report one phone country instead of six.
    /// </remarks>
    public static void UseRepositoryAsWorkingDirectory() => Environment.CurrentDirectory = Root;

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CameywareOrder.csproj")))
                return dir.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find the repository root (no CameywareOrder.csproj above " +
            $"{AppContext.BaseDirectory}). A harness cannot run without the shipped Settings tree.");
    }

    /// <summary>A folder under the repository's ignored scratch area, created on demand.</summary>
    public static string ScratchDirectory(string name)
    {
        var path = Path.Combine(Root, "Tests", ".artifacts", name);
        Directory.CreateDirectory(path);
        return path;
    }
}
