using Payments.Domain.Payments;
using Shop.SharedKernel;
using Shouldly;

namespace Payments.UnitTests;

public class PaymentTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static Payment NewPayment() =>
        Payment.Start(Guid.NewGuid(), Guid.NewGuid(), Money.Usd(1099.99m), "Fake", "fake_session", "https://pay.test/x",
            Now, Now.AddMinutes(30));

    private static Payment InStatus(PaymentStatus status)
    {
        var p = NewPayment();
        switch (status)
        {
            case PaymentStatus.Succeeded: p.Succeed(Now).IsSuccess.ShouldBeTrue(); break;
            case PaymentStatus.Failed: p.Fail("declined", Now).IsSuccess.ShouldBeTrue(); break;
            case PaymentStatus.Expired: p.Expire(Now).IsSuccess.ShouldBeTrue(); break;
        }

        p.ClearDomainEvents();
        return p;
    }

    [Fact]
    public void Start_opens_a_pending_payment_without_events()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var p = Payment.Start(orderId, customerId, Money.Usd(12.5m), "Stripe", "cs_test_1", "https://checkout.test/1",
            Now, Now.AddMinutes(30));

        p.Id.ShouldNotBe(Guid.Empty);
        p.OrderId.ShouldBe(orderId);
        p.CustomerId.ShouldBe(customerId);
        p.Amount.ShouldBe(Money.Usd(12.5m));
        p.Status.ShouldBe(PaymentStatus.Pending);
        p.Provider.ShouldBe("Stripe");
        p.ProviderSessionId.ShouldBe("cs_test_1");
        p.CheckoutUrl.ShouldBe("https://checkout.test/1");
        p.CreatedAtUtc.ShouldBe(Now);
        p.ExpiresAtUtc.ShouldBe(Now.AddMinutes(30));
        p.CompletedAtUtc.ShouldBeNull();
        p.FailureReason.ShouldBeNull();
        p.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PaymentStatus.Succeeded)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Expired)]
    public void Transitions_only_from_Pending(PaymentStatus finished)
    {
        var p = InStatus(finished);
        var completedAt = p.CompletedAtUtc;
        var later = Now.AddMinutes(5);

        Result[] attempts = [p.Succeed(later), p.Fail("declined", later), p.Expire(later)];

        attempts.ShouldAllBe(r => r.IsFailure && r.Error.Code == "payments.invalid_transition");
        p.Status.ShouldBe(finished);
        p.CompletedAtUtc.ShouldBe(completedAt);
        p.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Each_transition_raises_its_domain_event()
    {
        var succeeded = NewPayment();
        succeeded.Succeed(Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        succeeded.Status.ShouldBe(PaymentStatus.Succeeded);
        succeeded.CompletedAtUtc.ShouldBe(Now.AddMinutes(1));
        succeeded.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<PaymentSucceededDomainEvent>().Payment.ShouldBe(succeeded);

        var failed = NewPayment();
        failed.Fail("declined", Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        failed.Status.ShouldBe(PaymentStatus.Failed);
        failed.FailureReason.ShouldBe("declined");
        failed.CompletedAtUtc.ShouldBe(Now.AddMinutes(2));
        failed.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<PaymentFailedDomainEvent>().Payment.ShouldBe(failed);

        var expired = NewPayment();
        expired.Expire(Now.AddMinutes(3)).IsSuccess.ShouldBeTrue();
        expired.Status.ShouldBe(PaymentStatus.Expired);
        expired.CompletedAtUtc.ShouldBe(Now.AddMinutes(3));
        expired.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<PaymentExpiredDomainEvent>().Payment.ShouldBe(expired);
    }
}
