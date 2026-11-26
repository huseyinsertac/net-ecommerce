using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Catalog.Domain.Reservations;
using Microsoft.EntityFrameworkCore;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Outbox;
using Shop.Infrastructure.Common.Persistence;

namespace Catalog.Infrastructure.Persistence;

public sealed class CatalogDbContext(
    DbContextOptions<CatalogDbContext> options,
    IDomainEventDispatcher dispatcher,
    PendingIntegrationEvents pendingEvents,
    IntegrationEventTypeRegistry registry) : ModuleDbContext(options, dispatcher, pendingEvents, registry)
{
    public override string Schema => "catalog";

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
    }
}
