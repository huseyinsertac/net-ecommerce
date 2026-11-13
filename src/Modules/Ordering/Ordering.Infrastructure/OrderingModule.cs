using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions;
using Shop.Infrastructure.Common.Modules;
using Shop.Infrastructure.Common.Persistence;
using Ordering.Infrastructure.Persistence;

namespace Ordering.Infrastructure;

public sealed class OrderingModule : IModule
{
    public string Name => "ordering";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<OrderingDbContext>(configuration);
        services.AddApplicationHandlers(typeof(Ordering.Application.AssemblyReference).Assembly);
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGroup($"/api/{Name}");
    }
}
