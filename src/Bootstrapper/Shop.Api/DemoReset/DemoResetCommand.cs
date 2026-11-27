namespace Shop.Api.DemoReset;

/// <summary>
/// <c>dotnet Shop.Api.dll reset-demo</c>: migrates, wipes and re-seeds the demo with the built app's services, then
/// exits. The web server and the hosted services (outbox processors, expiry job, nightly timer) are never started.
/// </summary>
public static class DemoResetCommand
{
    public const string Name = "reset-demo";

    /// <summary>The process exit code: 0 on success, 1 (with an Error log) on failure.</summary>
    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoResetCommand).FullName!);
        try
        {
            logger.LogInformation("Demo reset requested from the command line");
            await ModuleMigrations.RunAllAsync(services, logger, ct);
            await services.GetRequiredService<DemoDataReset>().ResetAsync(ct);
            logger.LogInformation("Demo reset from the command line completed");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Demo reset from the command line failed");
            return 1;
        }
    }
}
