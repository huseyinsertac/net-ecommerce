using System.Net;
using System.Net.Http.Json;
using Payments.Contracts.Events;
using Payments.Domain.Payments;
using Shouldly;
using static Shop.IntegrationTests.Ordering.OrderingTestClient;
using static Shop.IntegrationTests.Payments.PaymentsTestClient;

namespace Shop.IntegrationTests.Payments;

[Collection(ShopApiCollection.Name)]
public sealed class SimulatedPaymentTests(ShopApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> PayAsync(HttpClient client, Guid orderId, bool succeed) =>
        client.PostAsJsonAsync($"/api/payments/simulated/{orderId}", new { succeed }, Ct);

    [Fact]
    public async Task Simulated_success_publishes_PaymentSucceeded()
    {
        var (shopper, orderId) = await PlaceOrderAsync(factory, Ct);
        using var _ = shopper;
        var pending = await WaitForPaymentAsync(factory.Services, orderId, Ct);

        var response = await PayAsync(shopper.Client, orderId, succeed: true);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
        var paid = (await FindAsync(factory.Services, orderId, Ct))!;
        paid.Status.ShouldBe(PaymentStatus.Succeeded);
        paid.CompletedAtUtc.ShouldNotBeNull();
        (await OutboxEventsAsync<PaymentSucceeded>(factory.Services, Ct))
            .Where(e => e.OrderId == orderId).ShouldHaveSingleItem().PaymentId.ShouldBe(pending.Id);

        // Paying again is a no-op: no second event, the payment stays Succeeded.
        (await PayAsync(shopper.Client, orderId, succeed: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await FindAsync(factory.Services, orderId, Ct))!.Status.ShouldBe(PaymentStatus.Succeeded);
        (await OutboxEventsAsync<PaymentSucceeded>(factory.Services, Ct)).Count(e => e.OrderId == orderId).ShouldBe(1);
        (await OutboxEventsAsync<PaymentFailed>(factory.Services, Ct)).ShouldNotContain(e => e.OrderId == orderId);
    }

    [Fact]
    public async Task Simulated_decline_publishes_PaymentFailed()
    {
        var (shopper, orderId) = await PlaceOrderAsync(factory, Ct);
        using var _ = shopper;
        var pending = await WaitForPaymentAsync(factory.Services, orderId, Ct);

        var response = await PayAsync(shopper.Client, orderId, succeed: false);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
        var failed = (await FindAsync(factory.Services, orderId, Ct))!;
        failed.Status.ShouldBe(PaymentStatus.Failed);
        failed.FailureReason.ShouldBe("declined");
        var e = (await OutboxEventsAsync<PaymentFailed>(factory.Services, Ct)).Where(x => x.OrderId == orderId).ShouldHaveSingleItem();
        e.PaymentId.ShouldBe(pending.Id);
        e.Reason.ShouldBe("declined");
    }

    [Fact]
    public async Task Simulated_payment_for_someone_elses_order_is_404()
    {
        var (owner, orderId) = await PlaceOrderAsync(factory, Ct);
        using var _ = owner;
        await WaitForPaymentAsync(factory.Services, orderId, Ct);
        using var stranger = await SignInAsync(factory, Ct);

        var response = await PayAsync(stranger.Client, orderId, succeed: true);

        var problem = await ProblemAsync(response, HttpStatusCode.NotFound, Ct);
        problem.GetProperty("type").GetString().ShouldBe("payments.payment_not_found");
        (await FindAsync(factory.Services, orderId, Ct))!.Status.ShouldBe(PaymentStatus.Pending);

        var unknown = await PayAsync(stranger.Client, Guid.NewGuid(), succeed: true);
        (await ProblemAsync(unknown, HttpStatusCode.NotFound, Ct)).GetProperty("type").GetString()
            .ShouldBe("payments.payment_not_found");
    }

    [Fact]
    public async Task Simulated_payment_requires_sign_in()
    {
        using var anonymous = factory.CreateClient();

        var response = await PayAsync(anonymous, Guid.NewGuid(), succeed: true);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
