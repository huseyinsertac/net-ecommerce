using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions.Events;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Persistence;
using Shouldly;

namespace Shop.Infrastructure.Common.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ModuleRoutingTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<ServiceProvider> BuildTwoModules(Action<IServiceCollection>? configure = null) =>
        TestHost.BuildAsync(postgres, (s, configuration) =>
        {
            s.AddModuleDbContext<TestModule2DbContext>(configuration);
            configure?.Invoke(s);
        }, Ct);

    private static async Task<List<Guid>> OutboxIds<TContext>(IServiceProvider sp) where TContext : ModuleDbContext
    {
        await using var scope = sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TContext>().OutboxMessages.Select(m => m.Id).ToListAsync(Ct);
    }

    [Fact]
    public async Task TwoModules_PublishFromEach_LandsInOwnOutbox()
    {
        await using var sp = await BuildTwoModules();
        var fromModule1 = new TestEvent("from-1");
        var fromModule2 = new TestEvent("from-2");

        await using (var scope = sp.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IEventPublisher>().Publish(fromModule1);
            await scope.ServiceProvider.GetRequiredService<TestModuleDbContext>().SaveChangesAsync(Ct);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IEventPublisher>().Publish(fromModule2);
            await scope.ServiceProvider.GetRequiredService<TestModule2DbContext>().SaveChangesAsync(Ct);
        }

        (await OutboxIds<TestModuleDbContext>(sp)).ShouldBe([fromModule1.Id]);
        (await OutboxIds<TestModule2DbContext>(sp)).ShouldBe([fromModule2.Id]);
    }

    [Fact]
    public async Task PublishedEventsDrainedBySecondModuleInSameScope_Throws()
    {
        await using var sp = await BuildTwoModules();
        await using var scope = sp.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var module1 = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
        var module2 = scope.ServiceProvider.GetRequiredService<TestModule2DbContext>();

        publisher.Publish(new TestEvent("first"));
        await module1.SaveChangesAsync(Ct);
        await module2.SaveChangesAsync(Ct); // nothing pending: must not trip the guard

        publisher.Publish(new TestEvent("second"));
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => module2.SaveChangesAsync(Ct));
        ex.Message.ShouldContain(nameof(TestModuleDbContext));
        ex.Message.ShouldContain(nameof(TestModule2DbContext));
        (await OutboxIds<TestModule2DbContext>(sp)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Bus_HandlersFromDifferentModules_RunInSeparateScopes()
    {
        await using var sp = await BuildTwoModules(s => s
            .AddScoped<ScopeMarker>()
            .AddInboxHandler<TestEvent, Module1ScopeHandler, TestModuleDbContext>()
            .AddInboxHandler<TestEvent, Module2ScopeHandler, TestModule2DbContext>());

        await sp.GetRequiredService<IEventBus>().PublishAsync(new TestEvent("fan-out"), Ct);

        var probe = sp.GetRequiredService<HandlerProbe>();
        probe.ScopeIds.Count.ShouldBe(2);
        probe.ScopeIds[nameof(Module1ScopeHandler)].ShouldNotBe(probe.ScopeIds[nameof(Module2ScopeHandler)]);
    }
}
