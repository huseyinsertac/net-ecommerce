using System.Reflection;

namespace Shop.Api;

/// <summary>Build and runtime identity of the running API.</summary>
public sealed record SystemInfoDto(string Name, string Version, string Environment);

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/system/info", (IConfiguration configuration, IWebHostEnvironment environment) =>
            new SystemInfoDto(configuration["App:Name"] ?? "VoltLane", ResolveVersion(), environment.EnvironmentName));

        return app;
    }

    private static string ResolveVersion()
    {
        var fromEnv = Environment.GetEnvironmentVariable("APP_VERSION");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        var informational = typeof(SystemEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(informational) ? "0.0.0" : informational;
    }
}
