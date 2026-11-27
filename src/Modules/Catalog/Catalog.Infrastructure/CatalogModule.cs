using Catalog.Application.Abstractions;
using Catalog.Infrastructure.Endpoints;
using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Reading;
using Catalog.Infrastructure.Reservations;
using Catalog.Infrastructure.Seeding;
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
        services.AddScoped<ICatalogReader, CatalogReader>();
        services.AddScoped<Catalog.Contracts.Variants.ICatalogVariantQuery, CatalogVariantQuery>();
        services.AddScoped<IModuleSeeder, CatalogSeeder>();
        services.AddScoped<IStockReservationRepository, StockReservationRepository>();
        services.AddApplicationHandlers(typeof(Catalog.Application.AssemblyReference).Assembly);
        services.AddInboxHandlers<CatalogDbContext>(typeof(Catalog.Application.AssemblyReference).Assembly);
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        CatalogEndpoints.Map(app.MapGroup($"/api/{Name}"));
    }
}
