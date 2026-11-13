using Shop.SharedKernel;

namespace Shop.Application.Abstractions.Messaging;

public interface ICommand;

public interface ICommand<TResponse>;

public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    Task<Result> Handle(TCommand command, CancellationToken ct);
}

public interface ICommandHandler<in TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResponse> where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken ct);
}

public interface IDispatcher
{
    Task<Result> Send(ICommand command, CancellationToken ct = default);
    Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default);
    Task<Result<TResponse>> Query<TResponse>(IQuery<TResponse> query, CancellationToken ct = default);
}

public delegate Task<TResult> HandlerDelegate<TResult>();

public interface IPipelineBehavior<in TRequest, TResult> where TResult : Result
{
    Task<TResult> Handle(TRequest request, HandlerDelegate<TResult> next, CancellationToken ct);
}
