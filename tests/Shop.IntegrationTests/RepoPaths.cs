namespace Shop.IntegrationTests;

public static class RepoPaths
{
    /// <summary>The repository root: the nearest ancestor of the test binaries that contains <c>VoltLane.slnx</c>.</summary>
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "VoltLane.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate VoltLane.slnx above " + AppContext.BaseDirectory);
    }
}
