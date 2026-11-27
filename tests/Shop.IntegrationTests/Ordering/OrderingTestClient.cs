using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Shop.IntegrationTests.Catalog;
using Shouldly;

namespace Shop.IntegrationTests.Ordering;

/// <summary>Helpers for the ordering API: signed-in shoppers, purchasable products, basket lines and checkout calls.</summary>
public static class OrderingTestClient
{
    public const string IdempotencyHeader = "Idempotency-Key";

    public static object ValidAddress { get; } = new
    {
        fullName = "Ada Lovelace",
        line1 = "12 Analytical Row",
        line2 = (string?)null,
        city = "London",
        postalCode = "N1 9GU",
        country = "gb",
    };

    /// <summary>A freshly registered user: a client carrying its bearer, its id and email.</summary>
    public static async Task<Shopper> SignInAsync(ShopApiFactory factory, CancellationToken ct)
    {
        var client = factory.CreateClient();
        var email = AuthTestClient.NewEmail();
        var response = await AuthTestClient.RegisterAsync(client, email);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        AuthTestClient.Bearer(client, doc.RootElement.GetProperty("accessToken").GetString()!);
        var userId = doc.RootElement.GetProperty("user").GetProperty("id").GetGuid();
        return new Shopper(client, userId, email);
    }

    /// <summary>An Active laptop (variants Silver 1099.99 and Black 1299.50) with <paramref name="stock"/> units of each.</summary>
    public static async Task<Product> SeedLaptopAsync(IServiceProvider services, CancellationToken ct, int stock = 5)
    {
        var suffix = CatalogTestData.NewSuffix();
        var category = CatalogTestData.NewCategory($"laptops-{suffix}", "Laptops", CatalogTestData.LaptopSchema);
        var product = CatalogTestData.Laptop(category, suffix, stockOnHand: stock);
        product.Publish().IsSuccess.ShouldBeTrue();

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Categories.Add(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return product;
    }

    public static Guid Silver(Product laptop) => laptop.Variants.Single(v => v.Options["color"] == "Silver").Id;

    public static Guid Black(Product laptop) => laptop.Variants.Single(v => v.Options["color"] == "Black").Id;

    public static async Task AddToBasketAsync(HttpClient client, Guid variantId, int quantity, CancellationToken ct)
    {
        var response = await client.PutAsJsonAsync($"/api/basket/items/{variantId}", new { quantity }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>POSTs a checkout. <paramref name="key"/> null sends no Idempotency-Key header.</summary>
    public static Task<HttpResponseMessage> CheckoutAsync(
        HttpClient client, string? key, decimal expectedTotal, CancellationToken ct, object? address = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ordering/checkout")
        {
            Content = JsonContent.Create(new { shippingAddress = address ?? ValidAddress, expectedTotal }),
        };
        if (key is not null)
        {
            request.Headers.Add(IdempotencyHeader, key);
        }

        return client.SendAsync(request, ct);
    }

    /// <summary>A checkout that must be accepted; returns the order id and number.</summary>
    public static async Task<(Guid OrderId, string OrderNumber)> PlaceAsync(
        HttpClient client, decimal expectedTotal, CancellationToken ct, Guid? key = null)
    {
        var response = await CheckoutAsync(client, (key ?? Guid.NewGuid()).ToString(), expectedTotal, ct);
        return await AcceptedAsync(response, ct);
    }

    public static async Task<(Guid OrderId, string OrderNumber)> AcceptedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, raw);
        using var doc = JsonDocument.Parse(raw);
        return (doc.RootElement.GetProperty("orderId").GetGuid(), doc.RootElement.GetProperty("orderNumber").GetString()!);
    }

    public static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.ShouldBe(status, raw);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        return JsonDocument.Parse(raw).RootElement.Clone();
    }
}

public sealed record Shopper(HttpClient Client, Guid UserId, string Email) : IDisposable
{
    public void Dispose() => Client.Dispose();
}
