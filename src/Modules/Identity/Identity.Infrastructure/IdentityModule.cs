using Identity.Application.Abstractions;
using Identity.Infrastructure.Endpoints;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Security;
using Identity.Infrastructure.Seeding;
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
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
        services.AddSingleton<IRefreshTokenFactory, RefreshTokenFactory>();
        services.AddSingleton<IRefreshTokenPolicy, ConfiguredRefreshTokenPolicy>();
        services.AddScoped<IModuleSeeder, IdentitySeeder>();
        services.AddShopAuthentication(configuration);
        services.AddApplicationHandlers(typeof(Identity.Application.AssemblyReference).Assembly);
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        IdentityEndpoints.Map(app.MapGroup($"/api/{Name}"));
    }
}
