using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shop.Application.Abstractions.Behaviors;
using Shop.Application.Abstractions.Events;
using Shop.Application.Abstractions.Messaging;

namespace Shop.Application.Abstractions;

public static class DependencyInjection
{
    /// <summary>Registers the dispatcher plus the Logging (outermost) and Validation behaviors.</summary>
    public static IServiceCollection AddDispatcher(this IServiceCollection services)
    {
        services.TryAddScoped<IDispatcher, Dispatcher>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        return services;
    }

    /// <summary>
    /// Scans the assembly, including non-public types, for command/query/domain-event handlers and FluentValidation validators.
    /// Integration-event handlers are deliberately NOT registered here: they must only ever run wrapped in the idempotent inbox,
    /// so the module registers them with <c>AddInboxHandler</c>/<c>AddInboxHandlers</c> (Shop.Infrastructure.Common).
    /// </summary>
    public static IServiceCollection AddApplicationHandlers(this IServiceCollection services, Assembly assembly)
    {
        Type[] scanned =
        [
            typeof(ICommandHandler<>), typeof(ICommandHandler<,>), typeof(IQueryHandler<,>),
            typeof(IDomainEventHandler<>), typeof(IValidator<>),
        ];

        var types = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (var type in types)
        {
            foreach (var i in type.GetInterfaces())
            {
                if (i.IsGenericType && scanned.Contains(i.GetGenericTypeDefinition()))
                {
                    services.AddScoped(i, type);
                }
            }
        }

        return services;
    }
}
