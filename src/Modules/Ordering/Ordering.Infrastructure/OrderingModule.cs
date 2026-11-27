using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application.Abstractions;
using Ordering.Contracts.Orders;
using Ordering.Infrastructure.Endpoints;
using Ordering.Infrastructure.Persistence;
using Ordering.Infrastructure.Reading;
using Ordering.Infrastructure.Seeding;
using Shop.Application.Abstractions;
using Shop.Infrastructure.Common.Modules;
using Shop.Infrastructure.Common.Persistence;

namespace Ordering.Infrastructure;

public sealed class OrderingModule : IModule
{
    public string Name => "ordering";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<OrderingDbContext>(configuration);
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderPaymentQuery, OrderPaymentQuery>();
        // Runs after the catalog seeder (Ordering is registered after Catalog): the orders use catalog variants.
        services.AddScoped<IModuleSeeder, OrderingSeeder>();
        services.AddApplicationHandlers(typeof(Ordering.Application.AssemblyReference).Assembly);
        services.AddInboxHandlers<OrderingDbContext>(typeof(Ordering.Application.AssemblyReference).Assembly);
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        OrderingEndpoints.Map(app.MapGroup($"/api/{Name}"));
        OrderingAdminEndpoints.Map(app.MapGroup($"/api/{Name}/admin"));
    }
}
