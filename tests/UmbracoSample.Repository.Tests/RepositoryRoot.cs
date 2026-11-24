namespace UmbracoSample.Repository.Tests;

/// <summary>Locates the repository root so policy tests can read committed files.</summary>
internal static class RepositoryRoot
{
    public static DirectoryInfo Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UmbracoSample.slnx")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException("Repository root (UmbracoSample.slnx) not found above the test output folder.");
    }

    /// <summary>Paths tracked by Git, relative to the repository root, with forward slashes.</summary>
    public static IEnumerable<string> TrackedFiles()
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git", "ls-files -z")
        {
            WorkingDirectory = Find().FullName,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using var git = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        var output = git.StandardOutput.ReadToEnd();

        // ES-043: a bounded wait; ls-files on this repository takes milliseconds.
        if (!git.WaitForExit(TimeSpan.FromSeconds(30)) || git.ExitCode != 0)
        {
            throw new InvalidOperationException($"git ls-files failed with exit code {git.ExitCode}.");
        }

        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    public static IEnumerable<FileInfo> Files(string searchPattern) =>
        Find()
            .EnumerateFiles(searchPattern, SearchOption.AllDirectories)
            .Where(file => !IsIgnored(file.FullName));

    private static bool IsIgnored(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
