namespace Shop.Api.DemoReset;

public static class DemoResetRegistration
{
    /// <summary>
    /// Registers <see cref="DemoDataReset"/> (used by the <c>reset-demo</c> command) and, only when
    /// <c>DemoReset:Enabled</c> is true, the <see cref="NightlyResetService"/>.
    /// </summary>
    public static IServiceCollection AddDemoReset(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(DemoResetOptions.SectionName);
        services.AddOptions<DemoResetOptions>()
            .Bind(section)
            .Validate(o => o.TimeOfDayUtc >= TimeSpan.Zero && o.TimeOfDayUtc < TimeSpan.FromDays(1),
                "DemoReset:TimeOfDayUtc must be a time of day between 00:00 and 23:59:59.")
            .Validate(o => o.StatementTimeout >= TimeSpan.FromMilliseconds(1),
                "DemoReset:StatementTimeout must be positive.")
            .ValidateOnStart();
        services.AddSingleton<DemoDataReset>();

        if (section.GetValue(nameof(DemoResetOptions.Enabled), false))
        {
            services.AddHostedService<NightlyResetService>();
        }

        return services;
    }
}
