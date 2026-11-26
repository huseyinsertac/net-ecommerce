using Basket.Application.Abstractions;
using Basket.Domain.Baskets;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Shop.IntegrationTests.Basket;

[Collection(ShopApiCollection.Name)]
public sealed class BasketPersistenceTests(ShopApiFactory factory)
{
    private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Services
    {
        get
        {
            using var _ = factory.CreateClient(); // forces host start (and startup migration)
            return factory.Services;
        }
    }

    private async Task<BasketOwner> SavedBasketAsync(params Guid[] variants)
    {
        var owner = BasketOwner.ForUser(Guid.NewGuid());
        await using var scope = Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBasketRepository>();
        var basket = BasketAggregate.Create(owner, Now);
        foreach (var v in variants)
        {
            basket.SetQuantity(v, 1, Now).IsSuccess.ShouldBeTrue();
        }

        repo.Add(basket);
        await repo.SaveChangesAsync(Ct);
        return owner;
    }

    [Fact]
    public async Task RoundTrip_ByUserAndByAnonymousOwner()
    {
        var variant = Guid.NewGuid();
        var userOwner = await SavedBasketAsync(variant);
        var anonOwner = BasketOwner.ForAnonymous(Guid.NewGuid());
        await using (var scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IBasketRepository>();
            var anon = BasketAggregate.Create(anonOwner, Now);
            anon.SetQuantity(variant, 4, Now).IsSuccess.ShouldBeTrue();
            repo.Add(anon);
            await repo.SaveChangesAsync(Ct);
        }

        await using var read = Services.CreateAsyncScope();
        var reader = read.ServiceProvider.GetRequiredService<IBasketRepository>();
        var byUser = (await reader.GetAsync(userOwner, Ct)).ShouldNotBeNull();
        byUser.Owner.ShouldBe(userOwner);
        byUser.Lines.ShouldHaveSingleItem().VariantId.ShouldBe(variant);
        var byAnon = (await reader.GetAsync(anonOwner, Ct)).ShouldNotBeNull();
        byAnon.Owner.ShouldBe(anonOwner);
        byAnon.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(4);
        (await reader.GetAsync(BasketOwner.ForAnonymous(userOwner.UserId!.Value), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task ConcurrentUpdate_LoserGetsBasketConcurrencyException()
    {
        var owner = await SavedBasketAsync(Guid.NewGuid());
        await using var first = Services.CreateAsyncScope();
        await using var second = Services.CreateAsyncScope();
        var repoA = first.ServiceProvider.GetRequiredService<IBasketRepository>();
        var repoB = second.ServiceProvider.GetRequiredService<IBasketRepository>();
        var a = (await repoA.GetAsync(owner, Ct))!;
        var b = (await repoB.GetAsync(owner, Ct))!;

        a.SetQuantity(Guid.NewGuid(), 1, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        await repoA.SaveChangesAsync(Ct);
        b.SetQuantity(Guid.NewGuid(), 2, Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();

        await Should.ThrowAsync<BasketConcurrencyException>(() => repoB.SaveChangesAsync(Ct));

        // The repository forgot its stale state, so a reload in the same scope sees the winner's row.
        var reloaded = (await repoB.GetAsync(owner, Ct))!;
        reloaded.Lines.Count.ShouldBe(2);
    }

    [Fact]
    public async Task SecondBasketForSameUser_UniqueIndex_MapsToConcurrencyException()
    {
        var owner = await SavedBasketAsync();
        await using var scope = Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBasketRepository>();
        repo.Add(BasketAggregate.Create(owner, Now));

        await Should.ThrowAsync<BasketConcurrencyException>(() => repo.SaveChangesAsync(Ct));
    }
}
