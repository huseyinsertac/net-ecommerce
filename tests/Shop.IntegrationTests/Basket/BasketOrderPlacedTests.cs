using System.Net.Http.Json;
using Basket.Application.Baskets;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Contracts.Events;
using Shop.Infrastructure.Common.Events;
using Shouldly;
using static Shop.IntegrationTests.Ordering.OrderingTestClient;

namespace Shop.IntegrationTests.Basket;

[Collection(ShopApiCollection.Name)]
public sealed class BasketOrderPlacedTests(ShopApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Services
    {
        get
        {
            using var _ = factory.CreateClient(); // forces host start (and startup migration)
            return factory.Services;
        }
    }

    [Fact]
    public async Task OrderPlaced_removes_only_the_ordered_lines_from_the_basket()
    {
        var laptop = await SeedLaptopAsync(Services, Ct);
        var other = await SeedLaptopAsync(Services, Ct);
        using var shopper = await SignInAsync(factory, Ct);
        await AddToBasketAsync(shopper.Client, Silver(laptop), 2, Ct);
        await AddToBasketAsync(shopper.Client, Silver(other), 1, Ct);
        await Task.Delay(20, Ct);
        var placedAt = DateTime.UtcNow;
        await Task.Delay(20, Ct);
        // Added after the order was placed: kept even though the order has this variant (it was removed and re-added).
        await AddToBasketAsync(shopper.Client, Black(laptop), 1, Ct);

        var placed = new OrderPlaced(
            Guid.NewGuid(), shopper.UserId,
            [new OrderPlacedLine(Silver(laptop), 2), new OrderPlacedLine(Black(laptop), 1)],
            3499.48m, "USD") { OccurredOnUtc = placedAt };
        var bus = Services.GetRequiredService<IEventBus>();
        await bus.PublishAsync(placed, Ct);
        await bus.PublishAsync(placed, Ct); // a redelivery changes nothing

        var basket = (await shopper.Client.GetFromJsonAsync<BasketDto>("/api/basket", Ct))!;
        basket.Lines.Select(l => l.VariantId).ShouldBe([Silver(other), Black(laptop)], ignoreOrder: true);
    }

    [Fact]
    public async Task OrderPlaced_for_a_customer_without_a_basket_is_a_no_op()
    {
        var bus = Services.GetRequiredService<IEventBus>();

        await bus.PublishAsync(
            new OrderPlaced(Guid.NewGuid(), Guid.NewGuid(), [new OrderPlacedLine(Guid.NewGuid(), 1)], 1m, "USD"), Ct);
    }
}
