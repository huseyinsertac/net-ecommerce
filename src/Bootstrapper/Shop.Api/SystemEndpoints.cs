using System.Reflection;

namespace Shop.Api;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/system/info", (IConfiguration configuration, IWebHostEnvironment environment) => new
        {
            name = configuration["App:Name"] ?? "VoltLane",
            version = ResolveVersion(),
            environment = environment.EnvironmentName,
        });

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
