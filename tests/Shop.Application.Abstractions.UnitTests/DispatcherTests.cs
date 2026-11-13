using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;
using Shouldly;

namespace Shop.Application.Abstractions.UnitTests;

public sealed record PingCommand : ICommand<int>;
public sealed record PingQuery : IQuery<string>;
public sealed record UnhandledCommand : ICommand;

public sealed class PingHandler : ICommandHandler<PingCommand, int>
{
    public Task<Result<int>> Handle(PingCommand command, CancellationToken ct) => Task.FromResult(Result.Success(42));
}

public sealed class PingQueryHandler : IQueryHandler<PingQuery, string>
{
    public Task<Result<string>> Handle(PingQuery query, CancellationToken ct) => Task.FromResult(Result.Success("pong"));
}

internal sealed record InternalCommand : ICommand<string>;

internal sealed class InternalCommandHandler : ICommandHandler<InternalCommand, string>
{
    public Task<Result<string>> Handle(InternalCommand command, CancellationToken ct) => Task.FromResult(Result.Success("internal"));
}

public class DispatcherTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDispatcher();
        services.AddApplicationHandlers(typeof(DispatcherTests).Assembly);
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task Send_RoutesCommandToItsHandler()
    {
        using var sp = Build();
        var result = await sp.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>().Send(new PingCommand(), TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public async Task Query_RoutesQueryToItsHandler()
    {
        using var sp = Build();
        var result = await sp.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>().Query(new PingQuery(), TestContext.Current.CancellationToken);
        result.Value.ShouldBe("pong");
    }

    [Fact]
    public async Task Send_NoHandlerRegistered_Throws()
    {
        using var sp = Build();
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => sp.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>().Send(new UnhandledCommand(), TestContext.Current.CancellationToken));
        ex.Message.ShouldContain(nameof(UnhandledCommand));
    }

    [Fact]
    public async Task AddApplicationHandlers_RegistersInternalHandlers()
    {
        using var sp = Build();
        var result = await sp.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>().Send(new InternalCommand(), TestContext.Current.CancellationToken);
        result.Value.ShouldBe("internal");
    }
}
