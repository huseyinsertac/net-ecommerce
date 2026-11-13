using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions.Events;
using Shouldly;

namespace Shop.Infrastructure.Common.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class DomainEventDispatchTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<ServiceProvider> Build() =>
        TestHost.BuildAsync(postgres, s => s.AddScoped<IDomainEventHandler<TestAggregateCreated>, PublishOnCreated>(), Ct);

    [Fact]
    public async Task SaveChanges_DomainEventHandlerPublishes_OutboxRowSavedWithAggregate()
    {
        await using var sp = await Build();
        var aggregate = TestAggregate.Create("widget");

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            db.Aggregates.Add(aggregate);
            await db.SaveChangesAsync(Ct);
            aggregate.DomainEvents.ShouldBeEmpty();
        }

        await using var verify = sp.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<TestModuleDbContext>();
        (await verifyDb.Aggregates.CountAsync(Ct)).ShouldBe(1);
        var row = (await verifyDb.OutboxMessages.ToListAsync(Ct)).ShouldHaveSingleItem();
        row.Type.ShouldBe(typeof(TestEvent).FullName);
        row.Content.ShouldContain(aggregate.Id.ToString());
    }

    [Fact]
    public async Task SaveChanges_DomainEventHandlerThrows_NothingIsSaved()
    {
        await using var sp = await Build();
        sp.GetRequiredService<HandlerProbe>().ThrowInDomainHandler = true;

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            db.Aggregates.Add(TestAggregate.Create("widget"));
            await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync(Ct));
        }

        await using var verify = sp.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<TestModuleDbContext>();
        (await verifyDb.Aggregates.CountAsync(Ct)).ShouldBe(0);
        (await verifyDb.OutboxMessages.CountAsync(Ct)).ShouldBe(0);
    }
}
