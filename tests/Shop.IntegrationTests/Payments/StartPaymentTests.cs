using Catalog.Contracts.Events;
using Microsoft.Extensions.DependencyInjection;
using Payments.Contracts.Payments;
using Payments.Domain.Payments;
using Shop.Infrastructure.Common.Events;
using Shouldly;
using static Shop.IntegrationTests.Payments.PaymentsTestClient;

namespace Shop.IntegrationTests.Payments;

[Collection(ShopApiCollection.Name)]
public sealed class StartPaymentTests(ShopApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StockReserved_creates_one_pending_payment_with_simulated_url()
    {
        var (shopper, orderId) = await PlaceOrderAsync(factory, Ct);
        using var _ = shopper;

        var payment = await WaitForPaymentAsync(factory.Services, orderId, Ct);

        payment.Status.ShouldBe(PaymentStatus.Pending);
        payment.CustomerId.ShouldBe(shopper.UserId);
        payment.Amount.Amount.ShouldBe(1099.99m);
        payment.Amount.Currency.ShouldBe("USD");
        payment.Provider.ShouldBe("Fake");
        payment.ProviderSessionId.ShouldBe($"fake_{orderId:N}");
        payment.CheckoutUrl.ShouldBe($"http://test.local/pay/simulated/{orderId}");
        (payment.ExpiresAtUtc - payment.CreatedAtUtc).ShouldBe(TimeSpan.FromMinutes(5));

        // Redeliveries (the same message twice, and a second StockReserved for the order) open nothing new.
        var bus = factory.Services.GetRequiredService<IEventBus>();
        var again = new StockReserved(orderId);
        await bus.PublishAsync(again, Ct);
        await bus.PublishAsync(again, Ct);
        await bus.PublishAsync(new StockReserved(orderId), Ct);

        (await CountAsync(factory.Services, orderId, Ct)).ShouldBe(1);
        (await FindAsync(factory.Services, orderId, Ct))!.Id.ShouldBe(payment.Id);
    }

    [Fact]
    public async Task Checkout_query_reports_the_pending_payment_and_its_url()
    {
        var (shopper, orderId) = await PlaceOrderAsync(factory, Ct);
        using var _ = shopper;
        var payment = await WaitForPaymentAsync(factory.Services, orderId, Ct);

        await using var scope = factory.Services.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IPaymentCheckoutQuery>();

        var info = await query.GetByOrderAsync(orderId, Ct);
        info.ShouldNotBeNull();
        info.PaymentId.ShouldBe(payment.Id);
        info.Status.ShouldBe("Pending");
        info.CheckoutUrl.ShouldBe($"http://test.local/pay/simulated/{orderId}");
        info.ExpiresAtUtc.ShouldBe(payment.ExpiresAtUtc);

        (await query.GetByOrderAsync(Guid.NewGuid(), Ct)).ShouldBeNull();
    }
}
