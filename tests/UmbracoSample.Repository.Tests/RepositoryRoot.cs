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
