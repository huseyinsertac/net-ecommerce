using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions;
using Shop.Infrastructure.Common.Modules;
using Shop.Infrastructure.Common.Persistence;

namespace Identity.Infrastructure;

public sealed class IdentityModule : IModule
{
    public string Name => "identity";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityDbContext>(configuration);
        services.AddApplicationHandlers(typeof(Identity.Application.AssemblyReference).Assembly);
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGroup($"/api/{Name}");
    }
}
