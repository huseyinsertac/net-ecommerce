using Basket.Application.Abstractions;
using Basket.Contracts;
using Basket.Infrastructure.Endpoints;
using Basket.Infrastructure.Persistence;
using Basket.Infrastructure.Reading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions;
using Shop.Infrastructure.Common.Modules;
using Shop.Infrastructure.Common.Persistence;

namespace Basket.Infrastructure;

public sealed class BasketModule : IModule
{
    public string Name => "basket";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<BasketDbContext>(configuration);
        services.AddScoped<IBasketRepository, BasketRepository>();
        services.AddScoped<IBasketCheckoutQuery, BasketCheckoutQuery>();
        services.AddApplicationHandlers(typeof(Basket.Application.AssemblyReference).Assembly);
        services.AddInboxHandlers<BasketDbContext>(typeof(Basket.Application.AssemblyReference).Assembly);
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        BasketEndpoints.Map(app.MapGroup($"/api/{Name}"));
    }
}
