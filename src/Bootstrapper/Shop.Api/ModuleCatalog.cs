using Basket.Infrastructure;
using Catalog.Infrastructure;
using Identity.Infrastructure;
using Ordering.Infrastructure;
using Payments.Infrastructure;
using Shop.Infrastructure.Common.Modules;

namespace Shop.Api;

/// <summary>The modules composed into this host. An explicit list on purpose: no assembly scanning.</summary>
public static class ModuleCatalog
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new CatalogModule(),
        new BasketModule(),
        new OrderingModule(),
        new PaymentsModule(),
        new IdentityModule(),
    ];
}
