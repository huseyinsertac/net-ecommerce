using FluentValidation;

namespace Ordering.Application.Checkout;

/// <summary>
/// Request-shape checks, reported per field as <c>validation.failed</c>. The limits mirror
/// <see cref="Ordering.Domain.Orders.ShippingAddress.Create"/>, which stays the authority.
/// </summary>
internal sealed class PlaceOrderValidator : AbstractValidator<PlaceOrder>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.CustomerEmail).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.ExpectedTotal).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.ShippingAddress).NotNull();
        RuleFor(x => x.ShippingAddress!.FullName).NotEmpty().MaximumLength(100).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress!.Line1).NotEmpty().MaximumLength(100).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress!.Line2).MaximumLength(100).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress!.City).NotEmpty().MaximumLength(100).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress!.PostalCode).NotEmpty().MaximumLength(20).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress!.Country)
            .NotEmpty()
            .Must(c => c is null || c.Trim() is { Length: 2 } code && code.All(char.IsAsciiLetter))
            .WithMessage("Country must be a 2-letter code.")
            .When(x => x.ShippingAddress is not null);
    }
}
