using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Payments.Application.Abstractions;
using Payments.Application.Gateways;
using Payments.Contracts.Payments;
using Payments.Infrastructure.Endpoints;
using Payments.Infrastructure.Gateways;
using Payments.Infrastructure.Jobs;
using Payments.Infrastructure.Persistence;
using Payments.Infrastructure.Reading;
using Payments.Infrastructure.Webhooks;
using Shop.Application.Abstractions;
using Shop.Infrastructure.Common.Modules;
using Shop.Infrastructure.Common.Persistence;
using Stripe;

namespace Payments.Infrastructure;

public sealed class PaymentsModule : IModule
{
    public string Name => "payments";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PaymentsDbContext>(configuration);
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentCheckoutQuery, PaymentCheckoutQuery>();
        services.AddApplicationHandlers(typeof(Payments.Application.AssemblyReference).Assembly);
        services.AddInboxHandlers<PaymentsDbContext>(typeof(Payments.Application.AssemblyReference).Assembly);

        services.AddOptions<StripeOptions>().Bind(configuration.GetSection(StripeOptions.SectionName));
        services.AddOptions<PaymentsOptions>()
            .Bind(configuration.GetSection(PaymentsOptions.SectionName))
            .Configure(o => o.PublicBaseUrl = (configuration[PaymentsOptions.PublicBaseUrlKey] ?? "").TrimEnd('/'))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PaymentsOptions>, PaymentsOptionsValidation>();
        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<PaymentsOptions>>().Value;
            return new PaymentSessionSettings(o.Timeout, o.PublicBaseUrl);
        });

        // Exactly one gateway, chosen by configuration. An unknown provider registers none and fails validation on start.
        switch (ConfiguredProvider(configuration))
        {
            case PaymentProviders.Stripe:
                services.AddSingleton<IStripeClient>(sp =>
                    new StripeClient(sp.GetRequiredService<IOptions<StripeOptions>>().Value.SecretKey));
                services.AddSingleton<IPaymentGateway, StripeGateway>();
                break;
            case PaymentProviders.Fake:
                services.AddSingleton<IPaymentGateway, FakeGateway>();
                break;
        }

        services.AddHostedService<PaymentExpiryService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"/api/{Name}");
        // Raw configuration, not IOptions: options are validated on start, after the endpoints are mapped.
        var provider = ConfiguredProvider(app.ServiceProvider.GetRequiredService<IConfiguration>());
        if (provider == PaymentProviders.Stripe)
        {
            StripeWebhookEndpoint.Map(group);
        }
        else if (provider == PaymentProviders.Fake)
        {
            SimulatedPaymentEndpoints.Map(group);
        }
    }

    private static string ConfiguredProvider(IConfiguration configuration) =>
        configuration[$"{PaymentsOptions.SectionName}:{nameof(PaymentsOptions.Provider)}"] ?? PaymentProviders.Fake;
}
