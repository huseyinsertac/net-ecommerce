using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Shop.Application.Abstractions;

internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    // One cached wrapper per concrete request type; avoids dynamic and per-call reflection.
    private static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public Task<Result> Send(ICommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var wrapper = (CommandWrapper)Wrappers.GetOrAdd(command.GetType(),
            static t => Activator.CreateInstance(typeof(CommandWrapperImpl<>).MakeGenericType(t))!);
        return wrapper.Handle(command, services, ct);
    }

    public Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var wrapper = (CommandWrapper<TResponse>)Wrappers.GetOrAdd(command.GetType(),
            static t => Activator.CreateInstance(typeof(CommandWrapperImpl<,>).MakeGenericType(t, typeof(TResponse)))!);
        return wrapper.Handle(command, services, ct);
    }

    public Task<Result<TResponse>> Query<TResponse>(IQuery<TResponse> query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var wrapper = (QueryWrapper<TResponse>)Wrappers.GetOrAdd(query.GetType(),
            static t => Activator.CreateInstance(typeof(QueryWrapperImpl<,>).MakeGenericType(t, typeof(TResponse)))!);
        return wrapper.Handle(query, services, ct);
    }

    private static Task<TResult> Run<TRequest, TResult>(
        TRequest request, IServiceProvider sp, HandlerDelegate<TResult> handler, CancellationToken ct)
        where TResult : Result
    {
        // Behaviors resolve in registration order: Logging (outermost), Validation, then any others. Wrap innermost-first.
        var behaviors = sp.GetServices<IPipelineBehavior<TRequest, TResult>>().ToArray();
        var next = handler;
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var inner = next;
            next = () => behavior.Handle(request, inner, ct);
        }

        return next();
    }

    private static THandler Resolve<THandler>(IServiceProvider sp, Type requestType) where THandler : notnull =>
        sp.GetService<THandler>()
        ?? throw new InvalidOperationException($"No handler registered for {requestType.Name}.");

    private abstract class CommandWrapper
    {
        public abstract Task<Result> Handle(ICommand command, IServiceProvider sp, CancellationToken ct);
    }

    private sealed class CommandWrapperImpl<TCommand> : CommandWrapper where TCommand : ICommand
    {
        public override Task<Result> Handle(ICommand command, IServiceProvider sp, CancellationToken ct)
        {
            var handler = Resolve<ICommandHandler<TCommand>>(sp, typeof(TCommand));
            var typed = (TCommand)command;
            return Run<TCommand, Result>(typed, sp, () => handler.Handle(typed, ct), ct);
        }
    }

    private abstract class CommandWrapper<TResponse>
    {
        public abstract Task<Result<TResponse>> Handle(ICommand<TResponse> command, IServiceProvider sp, CancellationToken ct);
    }

    private sealed class CommandWrapperImpl<TCommand, TResponse> : CommandWrapper<TResponse> where TCommand : ICommand<TResponse>
    {
        public override Task<Result<TResponse>> Handle(ICommand<TResponse> command, IServiceProvider sp, CancellationToken ct)
        {
            var handler = Resolve<ICommandHandler<TCommand, TResponse>>(sp, typeof(TCommand));
            var typed = (TCommand)command;
            return Run<TCommand, Result<TResponse>>(typed, sp, () => handler.Handle(typed, ct), ct);
        }
    }

    private abstract class QueryWrapper<TResponse>
    {
        public abstract Task<Result<TResponse>> Handle(IQuery<TResponse> query, IServiceProvider sp, CancellationToken ct);
    }

    private sealed class QueryWrapperImpl<TQuery, TResponse> : QueryWrapper<TResponse> where TQuery : IQuery<TResponse>
    {
        public override Task<Result<TResponse>> Handle(IQuery<TResponse> query, IServiceProvider sp, CancellationToken ct)
        {
            var handler = Resolve<IQueryHandler<TQuery, TResponse>>(sp, typeof(TQuery));
            var typed = (TQuery)query;
            return Run<TQuery, Result<TResponse>>(typed, sp, () => handler.Handle(typed, ct), ct);
        }
    }
}
