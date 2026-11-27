using FluentValidation;
using Ordering.Application.Abstractions;
using Shop.Application.Abstractions.Messaging;
using Shop.Application.Abstractions.Paging;
using Shop.SharedKernel;

namespace Ordering.Application.Orders;

/// <summary>The customer's orders, newest first.</summary>
public sealed record GetMyOrders(Guid CustomerId, int Page = 1, int PageSize = GetMyOrders.DefaultPageSize)
    : IQuery<PagedResult<OrderSummaryDto>>
{
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 50;
}

internal sealed class GetMyOrdersValidator : AbstractValidator<GetMyOrders>
{
    public GetMyOrdersValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, GetMyOrders.MaxPageSize);
    }
}

internal sealed class GetMyOrdersHandler(IOrderRepository orders) : IQueryHandler<GetMyOrders, PagedResult<OrderSummaryDto>>
{
    public async Task<Result<PagedResult<OrderSummaryDto>>> Handle(GetMyOrders query, CancellationToken ct)
    {
        var page = await orders.GetPageForCustomerAsync(query.CustomerId, query.Page, query.PageSize, ct);
        return new PagedResult<OrderSummaryDto>(
            [.. page.Items.Select(o => o.ToSummary())], page.Page, page.PageSize, page.TotalCount);
    }
}
