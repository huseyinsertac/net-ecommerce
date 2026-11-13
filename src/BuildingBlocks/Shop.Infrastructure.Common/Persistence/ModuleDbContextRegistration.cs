using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shop.Application.Abstractions.Events;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Inbox;
using Shop.Infrastructure.Common.Outbox;
using Shop.SharedKernel;

namespace Shop.Infrastructure.Common.Persistence;

/// <summary>Every module DbContext registered through <see cref="ModuleDbContextRegistration.AddModuleDbContext{TContext}"/>.</summary>
public sealed class ModuleDatabaseRegistry
{
    private readonly List<Type> _contextTypes = [];

    public IReadOnlyList<Type> ContextTypes => _contextTypes;

    internal void Add(Type contextType)
    {
        if (!_contextTypes.Contains(contextType))
        {
            _contextTypes.Add(contextType);
        }
    }
}

public static class ModuleDbContextRegistration
{
    public const string ConnectionStringName = "Shop";

    /// <summary>
    /// Registers the module DbContext on PostgreSQL (snake_case, per-schema migrations history) and the hosted
    /// <see cref="OutboxProcessor{TContext}"/>. Published integration events reach this module's outbox through the
    /// scoped <see cref="PendingIntegrationEvents"/> registered by <see cref="AddEventing"/>.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : ModuleDbContext
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        // The migrations history table needs the instance Schema; ModuleDbContext.OnConfiguring adds it.
        services.AddDbContext<TContext>(o => o
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        services.AddOptions<OutboxOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<OutboxProcessor<TContext>>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxProcessor<TContext>>());

        GetOrAddSingleton<ModuleDatabaseRegistry>(services).Add(typeof(TContext));
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> wrapped in the idempotent <see cref="InboxHandler{TEvent,THandler,TContext}"/>.
    /// This is the ONLY way integration-event handlers reach the bus; <c>AddApplicationHandlers</c> deliberately skips them,
    /// so a handler can never be invoked both raw and through the inbox.
    /// </summary>
    public static IServiceCollection AddInboxHandler<TEvent, THandler, TContext>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
        where TContext : ModuleDbContext
    {
        services.TryAddScoped<THandler>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IIntegrationEventHandler<TEvent>, InboxHandler<TEvent, THandler, TContext>>());
        return services;
    }

    /// <summary>
    /// Scans an Application assembly (including internal types) and calls <see cref="AddInboxHandler{TEvent,THandler,TContext}"/>
    /// for every integration-event handler in it, so modules need not name internal handler types.
    /// </summary>
    public static IServiceCollection AddInboxHandlers<TContext>(this IServiceCollection services, Assembly assembly)
        where TContext : ModuleDbContext
    {
        var addInboxHandler = typeof(ModuleDbContextRegistration).GetMethod(nameof(AddInboxHandler))!;
        var handlers = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (var handler in handlers)
        {
            var eventTypes = handler.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>))
                .Select(i => i.GetGenericArguments()[0]);

            foreach (var eventType in eventTypes)
            {
                addInboxHandler.MakeGenericMethod(eventType, handler, typeof(TContext)).Invoke(null, [services]);
            }
        }

        return services;
    }

    /// <summary>
    /// Registers the in-process bus, the integration event type registry, the domain-event dispatcher and the single scoped
    /// <see cref="IEventPublisher"/> (<see cref="PendingIntegrationEvents"/>).
    /// </summary>
    public static IServiceCollection AddEventing(this IServiceCollection services, params Assembly[] contractsAssemblies)
    {
        var registry = GetOrAddSingleton<IntegrationEventTypeRegistry>(services);
        foreach (var assembly in contractsAssemblies)
        {
            registry.Register(assembly);
        }

        services.TryAddSingleton<IEventBus, InProcessEventBus>();
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.TryAddScoped<PendingIntegrationEvents>();
        services.TryAddScoped<IEventPublisher>(sp => sp.GetRequiredService<PendingIntegrationEvents>());
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    private static T GetOrAddSingleton<T>(IServiceCollection services) where T : class, new()
    {
        var existing = services
            .Where(d => d.ServiceType == typeof(T) && !d.IsKeyedService)
            .Select(d => d.ImplementationInstance)
            .OfType<T>()
            .FirstOrDefault();
        if (existing is not null)
        {
            return existing;
        }

        var created = new T();
        services.AddSingleton(created);
        return created;
    }
}
