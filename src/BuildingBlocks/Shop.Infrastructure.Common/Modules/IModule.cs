using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shop.Infrastructure.Common.Modules;

/// <summary>Composition entry point of a module; the host calls it once at startup.</summary>
public interface IModule
{
    /// <summary>Lower-case module name; also the module's database schema and its route prefix (<c>/api/{Name}</c>).</summary>
    string Name { get; }

    void Register(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder app);
}
