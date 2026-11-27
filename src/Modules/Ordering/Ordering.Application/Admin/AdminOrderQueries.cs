using FluentValidation;
using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;
using Shop.Application.Abstractions.Messaging;
using Shop.Application.Abstractions.Paging;
using Shop.SharedKernel;

namespace Ordering.Application.Admin;

/// <summary>
/// Every customer's orders, newest first. <paramref name="Search"/> is an order number or number prefix ("VL-12", or just
/// digits) or part of a customer email.
/// </summary>
/// <param name="Status">An order status name, any case.</param>
public sealed record ListAdminOrders(string? Status, string? Search, int Page = 1, int PageSize = ListAdminOrders.DefaultPageSize)
    : IQuery<PagedResult<AdminOrderSummaryDto>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
    public const int MaxSearchLength = 100;

    internal static OrderStatus? ParseStatus(string? status) =>
        !string.IsNullOrWhiteSpace(status)
        && !char.IsDigit(status.Trim()[0])
        && Enum.TryParse<OrderStatus>(status.Trim(), ignoreCase: true, out var parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : null;
}

internal sealed class ListAdminOrdersValidator : AbstractValidator<ListAdminOrders>
{
    public ListAdminOrdersValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 100_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, ListAdminOrders.MaxPageSize);
        RuleFor(x => x.Search).MaximumLength(ListAdminOrders.MaxSearchLength);
        RuleFor(x => x.Status)
            .Must(s => string.IsNullOrWhiteSpace(s) || ListAdminOrders.ParseStatus(s) is not null)
            .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<OrderStatus>())}.");
    }
}

internal sealed class ListAdminOrdersHandler(IOrderRepository orders)
    : IQueryHandler<ListAdminOrders, PagedResult<AdminOrderSummaryDto>>
{
    public async Task<Result<PagedResult<AdminOrderSummaryDto>>> Handle(ListAdminOrders query, CancellationToken ct)
    {
        var filter = new AdminOrderFilter(ListAdminOrders.ParseStatus(query.Status), null, null);
        if (query.Search?.Trim() is { Length: > 0 } search)
        {
            filter = search.StartsWith(Order.NumberPrefix, StringComparison.OrdinalIgnoreCase)
                ? filter with { OrderNumberPrefix = search }
                : search.All(char.IsAsciiDigit)
                    ? filter with { OrderNumberPrefix = Order.NumberPrefix + search }
                    : filter with { EmailContains = search };
        }

        var page = await orders.GetPageForAdminAsync(filter, query.Page, query.PageSize, ct);
        return new PagedResult<AdminOrderSummaryDto>(
            [.. page.Items.Select(o => o.ToAdminSummary())], page.Page, page.PageSize, page.TotalCount);
    }
}

/// <summary>Any customer's order with its owner.</summary>
public sealed record GetAdminOrder(Guid OrderId) : IQuery<AdminOrderDto>;

internal sealed class GetAdminOrderHandler(IOrderRepository orders) : IQueryHandler<GetAdminOrder, AdminOrderDto>
{
    public async Task<Result<AdminOrderDto>> Handle(GetAdminOrder query, CancellationToken ct)
    {
        var order = await orders.GetReadOnlyAsync(query.OrderId, ct);
        return order is null ? OrderErrors.OrderNotFound() : order.ToAdminDto();
    }
}
