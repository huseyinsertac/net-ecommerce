using Catalog.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions;
using Shop.Infrastructure.Common.Modules;
using Shop.Infrastructure.Common.Persistence;

namespace Catalog.Infrastructure;

public sealed class CatalogModule : IModule
{
    public string Name => "catalog";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CatalogDbContext>(configuration);
        services.AddApplicationHandlers(typeof(Catalog.Application.AssemblyReference).Assembly);
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGroup($"/api/{Name}");
    }
}
