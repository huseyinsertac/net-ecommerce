using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions;
using Shop.Infrastructure.Common.Modules;
using Shop.Infrastructure.Common.Persistence;
using Payments.Infrastructure.Persistence;

namespace Payments.Infrastructure;

public sealed class PaymentsModule : IModule
{
    public string Name => "payments";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PaymentsDbContext>(configuration);
        services.AddApplicationHandlers(typeof(Payments.Application.AssemblyReference).Assembly);
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGroup($"/api/{Name}");
    }
}
