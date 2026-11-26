using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Basket.Application.Baskets;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shop.IntegrationTests.Catalog;
using Shouldly;

namespace Shop.IntegrationTests.Basket;

/// <summary>
/// The basket API end to end. The test host runs in Production, so <c>vl_basket</c> is Secure and a cookie-aware client
/// would not send it over http://localhost; like <see cref="AuthTestClient"/>, these tests read Set-Cookie and send the
/// Cookie header by hand.
/// </summary>
[Collection(ShopApiCollection.Name)]
public sealed class BasketEndpointTests(ShopApiFactory factory)
{
    private const string CookieName = "vl_basket";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Services
    {
        get
        {
            using var _ = factory.CreateClient(); // forces host start (and startup migration)
            return factory.Services;
        }
    }

    // ---------- helpers ----------

    /// <summary>Seeds a dataset and publishes both products, so all three variants are purchasable.</summary>
    private async Task<CatalogDataset> PublishedDatasetAsync()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        await MutateProductAsync(data.Laptop.Id, p => p.Publish());
        await MutateProductAsync(data.Headset.Id, p => p.Publish());
        return data;
    }

    private async Task MutateProductAsync(Guid productId, Func<Product, Shop.SharedKernel.Result> change)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var product = await db.Products.Include(p => p.Variants).SingleAsync(p => p.Id == productId, Ct);
        change(product).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
    }

    private async Task SqlAsync(FormattableString sql)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await db.Database.ExecuteSqlAsync(sql, Ct);
    }

    private static Guid Silver(CatalogDataset d) => d.Laptop.Variants.Single(v => v.Options["color"] == "Silver").Id;

    private static Guid Black(CatalogDataset d) => d.Laptop.Variants.Single(v => v.Options["color"] == "Black").Id;

    private static Guid Headset(CatalogDataset d) => d.Headset.Variants.Single().Id;

    private static HttpRequestMessage Request(HttpMethod method, string path, string? cookie = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={cookie}");
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid variantId, int quantity, string? cookie = null) =>
        client.SendAsync(Request(HttpMethod.Put, $"/api/basket/items/{variantId}", cookie, new { quantity }), Ct);

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string? cookie = null) =>
        client.SendAsync(Request(HttpMethod.Get, "/api/basket", cookie), Ct);

    private static Task<HttpResponseMessage> MergeAsync(HttpClient client, string? cookie = null) =>
        client.SendAsync(Request(HttpMethod.Post, "/api/basket/merge", cookie), Ct);

    private static string? SetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(CookieName + "=", StringComparison.Ordinal))
            : null;

    private static string? CookieValue(HttpResponseMessage response) =>
        SetCookie(response) is { } header ? header[(CookieName.Length + 1)..].Split(';')[0] : null;

    private static async Task<BasketDto> BasketAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<BasketDto>(Ct))!;
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        var raw = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(status, raw);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    /// <summary>An anonymous PUT that must succeed and mint the cookie; returns the cookie value.</summary>
    private static async Task<string> NewAnonymousBasketAsync(HttpClient client, Guid variantId, int quantity)
    {
        var response = await PutAsync(client, variantId, quantity);
        await BasketAsync(response);
        return CookieValue(response).ShouldNotBeNull();
    }

    // ---------- tests ----------

    [Fact]
    public async Task AnonymousPut_SetsBasketCookie_GetReturnsLineWithLivePrice()
    {
        var data = await PublishedDatasetAsync();
        using var client = factory.CreateClient();

        var put = await PutAsync(client, Silver(data), 2);

        var putBasket = await BasketAsync(put);
        putBasket.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(2);

        var header = SetCookie(put).ShouldNotBeNull();
        var attributes = header.ToLowerInvariant();
        attributes.ShouldContain("httponly");
        attributes.ShouldContain("secure"); // Production host
        attributes.ShouldContain("samesite=lax");
        attributes.ShouldContain("path=/api/basket");
        var expires = DateTimeOffset.Parse(header.Split(';').Select(p => p.Trim())
            .Single(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase))["expires=".Length..]);
        expires.ShouldBeInRange(DateTimeOffset.UtcNow.AddDays(29), DateTimeOffset.UtcNow.AddDays(31));
        var cookie = CookieValue(put)!;
        Guid.TryParse(cookie, out _).ShouldBeTrue();

        var basket = await BasketAsync(await GetAsync(client, cookie));

        var line = basket.Lines.ShouldHaveSingleItem();
        line.VariantId.ShouldBe(Silver(data));
        line.ProductSlug.ShouldBe(data.Laptop.Slug);
        line.ProductName.ShouldBe(data.Laptop.Name);
        line.OptionsLabel.ShouldBe("Silver · 512 GB");
        line.UnitPrice.ShouldBe(1099.99m);
        line.Quantity.ShouldBe(2);
        line.Available.ShouldBe(5);
        line.LineTotal.ShouldBe(2199.98m);
        line.Issue.ShouldBeNull();
        basket.ItemCount.ShouldBe(2);
        basket.Subtotal.ShouldBe(2199.98m);
        basket.HasIssues.ShouldBeFalse();
    }

    [Fact]
    public async Task Get_NoCookie_EmptyBasket_NoCookieIssued()
    {
        using var client = factory.CreateClient();

        var response = await GetAsync(client);

        var basket = await BasketAsync(response);
        basket.Lines.ShouldBeEmpty();
        basket.ItemCount.ShouldBe(0);
        basket.Subtotal.ShouldBe(0m);
        SetCookie(response).ShouldBeNull();
    }

    [Fact]
    public async Task Put_SecondWriteWithCookie_UpdatesSameBasket()
    {
        var data = await PublishedDatasetAsync();
        using var client = factory.CreateClient();
        var cookie = await NewAnonymousBasketAsync(client, Silver(data), 1);

        await BasketAsync(await PutAsync(client, Headset(data), 1, cookie));
        var updated = await BasketAsync(await PutAsync(client, Silver(data), 3, cookie));

        updated.Lines.Select(l => (l.VariantId, l.Quantity)).ShouldBe([(Silver(data), 3), (Headset(data), 1)]);
        updated.Subtotal.ShouldBe(3 * 1099.99m + 249m);
    }

    [Fact]
    public async Task Put_QuantityAboveAvailable_409InsufficientStock()
    {
        var data = await PublishedDatasetAsync();
        using var client = factory.CreateClient();

        var problem = await ProblemAsync(await PutAsync(client, Silver(data), 6), HttpStatusCode.Conflict);

        problem.GetProperty("type").GetString().ShouldBe("basket.insufficient_stock");
        problem.GetProperty("title").GetString().ShouldBe("Only 5 left.");

        // Fully reserved: nothing left at all.
        await SqlAsync($"UPDATE catalog.product_variants SET stock_reserved = stock_on_hand WHERE id = {Headset(data)}");
        var outOfStock = await ProblemAsync(await PutAsync(client, Headset(data), 1), HttpStatusCode.Conflict);
        outOfStock.GetProperty("type").GetString().ShouldBe("basket.insufficient_stock");
        outOfStock.GetProperty("title").GetString().ShouldBe("Out of stock.");
    }

    [Fact]
    public async Task Put_DraftProductVariant_404VariantUnavailable()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct); // products start as Draft
        using var client = factory.CreateClient();

        var draft = await ProblemAsync(await PutAsync(client, Silver(data), 1), HttpStatusCode.NotFound);
        var unknown = await ProblemAsync(await PutAsync(client, Guid.NewGuid(), 1), HttpStatusCode.NotFound);

        draft.GetProperty("type").GetString().ShouldBe("basket.variant_unavailable");
        unknown.GetProperty("type").GetString().ShouldBe("basket.variant_unavailable");
    }

    [Theory]
    [InlineData(11)]
    [InlineData(-1)]
    public async Task Put_QuantityOutOfRange_400InvalidQuantity(int quantity)
    {
        var data = await PublishedDatasetAsync();
        using var client = factory.CreateClient();

        var problem = await ProblemAsync(await PutAsync(client, Silver(data), quantity), HttpStatusCode.BadRequest);

        problem.GetProperty("type").GetString().ShouldBe("basket.invalid_quantity");
    }

    [Fact]
    public async Task Put_MissingQuantity_400_LineUntouched()
    {
        var data = await PublishedDatasetAsync();
        using var client = factory.CreateClient();
        var cookie = await NewAnonymousBasketAsync(client, Silver(data), 2);

        var response = await client.SendAsync(
            Request(HttpMethod.Put, $"/api/basket/items/{Silver(data)}", cookie, new { }), Ct);

        await ProblemAsync(response, HttpStatusCode.BadRequest);
        var line = (await BasketAsync(await GetAsync(client, cookie))).Lines.ShouldHaveSingleItem();
        line.Quantity.ShouldBe(2);
    }

    [Fact]
    public async Task Get_AfterVariantGoesOutOfStockAndPriceChanges_FlagsLine_UsesLivePrice_NoError()
    {
        var data = await PublishedDatasetAsync();
        using var client = factory.CreateClient();
        var cookie = await NewAnonymousBasketAsync(client, Silver(data), 2);
        await BasketAsync(await PutAsync(client, Black(data), 1, cookie));
        await BasketAsync(await PutAsync(client, Headset(data), 1, cookie));

        // Silver runs out and gets cheaper, Black only gets cheaper, the headset's product is archived.
        await SqlAsync($"UPDATE catalog.product_variants SET price = 999.00, stock_reserved = stock_on_hand WHERE id = {Silver(data)}");
        await SqlAsync($"UPDATE catalog.product_variants SET price = 1199.00 WHERE id = {Black(data)}");
        await MutateProductAsync(data.Headset.Id, p => p.Archive());

        var response = await GetAsync(client, cookie);

        var raw = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        var basket = JsonSerializer.Deserialize<BasketDto>(raw, JsonSerializerOptions.Web)!;

        var silver = basket.Lines.Single(l => l.VariantId == Silver(data));
        silver.Issue.ShouldBe(BasketLineIssue.InsufficientStock);
        silver.Available.ShouldBe(0);
        silver.UnitPrice.ShouldBe(999.00m);
        silver.LineTotal.ShouldBeNull();

        var black = basket.Lines.Single(l => l.VariantId == Black(data));
        black.Issue.ShouldBeNull();
        black.UnitPrice.ShouldBe(1199.00m);
        black.LineTotal.ShouldBe(1199.00m);

        var headset = basket.Lines.Single(l => l.VariantId == Headset(data));
        headset.Issue.ShouldBe(BasketLineIssue.Unavailable);
        headset.LineTotal.ShouldBeNull();

        basket.Subtotal.ShouldBe(1199.00m); // only the purchasable line
        basket.ItemCount.ShouldBe(4);
        basket.HasIssues.ShouldBeTrue();

        // The wire format is the camelCase string union the frontend types expect.
        raw.ShouldContain("\"issue\":\"insufficientStock\"");
        raw.ShouldContain("\"issue\":\"unavailable\"");
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("%E2%9C%93%3Cscript%3E")]
    [InlineData("1234")]
    public async Task Get_GarbageCookie_EmptyBasket_200(string cookie)
    {
        using var client = factory.CreateClient();

        var basket = await BasketAsync(await GetAsync(client, cookie));

        basket.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_UnknownCookie_EmptyBasket_200()
    {
        using var client = factory.CreateClient();

        var basket = await BasketAsync(await GetAsync(client, Guid.NewGuid().ToString()));

        basket.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task Put_GarbageCookie_MintsFreshCookie()
    {
        var data = await PublishedDatasetAsync();
        using var client = factory.CreateClient();

        var response = await PutAsync(client, Silver(data), 1, "garbage");

        (await BasketAsync(response)).Lines.ShouldHaveSingleItem();
        var minted = CookieValue(response).ShouldNotBeNull();
        Guid.TryParse(minted, out var id).ShouldBeTrue();
        id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task AuthenticatedRequest_IgnoresAnonymousCookie()
    {
        var data = await PublishedDatasetAsync();
        using var anonymous = factory.CreateClient();
        var cookie = await NewAnonymousBasketAsync(anonymous, Silver(data), 2);
        var (user, _, _) = await AuthTestClient.RegisterAndSignInAsync(factory);
        using var _ = user;

        // Reads come from the token, not from the (someone else's) cookie.
        (await BasketAsync(await GetAsync(user, cookie))).Lines.ShouldBeEmpty();

        // Writes go to the user's basket and never touch or re-issue the cookie.
        var put = await PutAsync(user, Headset(data), 1, cookie);
        (await BasketAsync(put)).Lines.ShouldHaveSingleItem().VariantId.ShouldBe(Headset(data));
        SetCookie(put).ShouldBeNull();

        (await BasketAsync(await GetAsync(user))).Lines.ShouldHaveSingleItem().VariantId.ShouldBe(Headset(data));
        var anonymousBasket = await BasketAsync(await GetAsync(anonymous, cookie));
        anonymousBasket.Lines.ShouldHaveSingleItem().VariantId.ShouldBe(Silver(data));
        anonymousBasket.Lines[0].Quantity.ShouldBe(2);
    }

    [Fact]
    public async Task Merge_SumsCapsDeletesAnonymous_ExpiresCookie_SecondMergeNoOp()
    {
        var data = await PublishedDatasetAsync();
        await SqlAsync($"UPDATE catalog.product_variants SET stock_on_hand = 50 WHERE id = {Silver(data)}");
        using var anonymous = factory.CreateClient();
        var cookie = await NewAnonymousBasketAsync(anonymous, Silver(data), 7);
        await BasketAsync(await PutAsync(anonymous, Headset(data), 1, cookie));

        var (user, _, _) = await AuthTestClient.RegisterAndSignInAsync(factory);
        using var _ = user;
        await BasketAsync(await PutAsync(user, Silver(data), 6));
        await BasketAsync(await PutAsync(user, Black(data), 1));

        var merge = await MergeAsync(user, cookie);

        var merged = await BasketAsync(merge);
        merged.Lines.Select(l => (l.VariantId, l.Quantity)).ShouldBe(
            [(Silver(data), 10), (Black(data), 1), (Headset(data), 1)], ignoreOrder: true);
        merged.ItemCount.ShouldBe(12);

        // Cookie expired with the same path, so the browser drops it.
        var expired = SetCookie(merge).ShouldNotBeNull().ToLowerInvariant();
        CookieValue(merge).ShouldBe(string.Empty);
        expired.ShouldContain("expires=thu, 01 jan 1970");
        expired.ShouldContain("path=/api/basket");

        // The anonymous basket is gone.
        (await BasketAsync(await GetAsync(anonymous, cookie))).Lines.ShouldBeEmpty();

        // Replaying the old cookie, or merging without one, changes nothing.
        var again = await BasketAsync(await MergeAsync(user, cookie));
        again.Lines.Select(l => (l.VariantId, l.Quantity)).ShouldBe(
            merged.Lines.Select(l => (l.VariantId, l.Quantity)), ignoreOrder: true);
        var noCookie = await BasketAsync(await MergeAsync(user));
        noCookie.ItemCount.ShouldBe(12);
        (await BasketAsync(await GetAsync(user))).ItemCount.ShouldBe(12);
    }

    [Fact]
    public async Task Merge_UserWithoutBasket_AdoptsAnonymousLines()
    {
        var data = await PublishedDatasetAsync();
        using var anonymous = factory.CreateClient();
        var cookie = await NewAnonymousBasketAsync(anonymous, Headset(data), 1);
        var (user, _, _) = await AuthTestClient.RegisterAndSignInAsync(factory);
        using var _ = user;

        var merged = await BasketAsync(await MergeAsync(user, cookie));

        merged.Lines.ShouldHaveSingleItem().VariantId.ShouldBe(Headset(data));
        (await BasketAsync(await GetAsync(anonymous, cookie))).Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task Merge_WithoutBearer_401()
    {
        var data = await PublishedDatasetAsync();
        using var client = factory.CreateClient();
        var cookie = await NewAnonymousBasketAsync(client, Silver(data), 1);

        var response = await MergeAsync(client, cookie);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await BasketAsync(await GetAsync(client, cookie))).Lines.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Get_InvalidOrExpiredBearer_401_NotAnonymousFallback()
    {
        var data = await PublishedDatasetAsync();
        using var client = factory.CreateClient();
        var cookie = await NewAnonymousBasketAsync(client, Silver(data), 1);

        foreach (var token in new[] { "garbage", ExpiredToken(Guid.NewGuid()) })
        {
            var request = Request(HttpMethod.Get, "/api/basket", cookie);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.SendAsync(request, Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task TwentyFirstDistinctVariant_409TooManyLines()
    {
        var variants = new List<Guid>();
        while (variants.Count < 21)
        {
            var data = await PublishedDatasetAsync();
            variants.AddRange([Silver(data), Black(data), Headset(data)]);
        }

        using var client = factory.CreateClient();
        var cookie = await NewAnonymousBasketAsync(client, variants[0], 1);
        foreach (var id in variants.Skip(1).Take(19))
        {
            await BasketAsync(await PutAsync(client, id, 1, cookie));
        }

        var problem = await ProblemAsync(await PutAsync(client, variants[20], 1, cookie), HttpStatusCode.Conflict);

        problem.GetProperty("type").GetString().ShouldBe("basket.too_many_lines");
        (await BasketAsync(await GetAsync(client, cookie))).Lines.Count.ShouldBe(20);
    }

    [Fact]
    public async Task DeleteItem_RemovesLine_DeleteBasket_204()
    {
        var data = await PublishedDatasetAsync();
        using var client = factory.CreateClient();
        var cookie = await NewAnonymousBasketAsync(client, Silver(data), 1);
        await BasketAsync(await PutAsync(client, Headset(data), 1, cookie));

        var afterDelete = await BasketAsync(await client.SendAsync(
            Request(HttpMethod.Delete, $"/api/basket/items/{Silver(data)}", cookie), Ct));
        afterDelete.Lines.ShouldHaveSingleItem().VariantId.ShouldBe(Headset(data));

        var clear = await client.SendAsync(Request(HttpMethod.Delete, "/api/basket", cookie), Ct);
        clear.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await BasketAsync(await GetAsync(client, cookie))).Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deletes_WithoutBasket_SucceedWithoutMintingCookie()
    {
        using var client = factory.CreateClient();

        var item = await client.SendAsync(Request(HttpMethod.Delete, $"/api/basket/items/{Guid.NewGuid()}", "garbage"), Ct);
        var all = await client.SendAsync(Request(HttpMethod.Delete, "/api/basket"), Ct);

        (await BasketAsync(item)).Lines.ShouldBeEmpty();
        all.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        SetCookie(item).ShouldBeNull();
        SetCookie(all).ShouldBeNull();
    }

    [Fact]
    public async Task DemoAdmin_Writes_403ReadOnly_NothingStored()
    {
        var data = await PublishedDatasetAsync();
        using var admin = Signed(User.CreateDemoAdmin(UniqueEmail(), DateTime.UtcNow));

        var put = await PutAsync(admin, Silver(data), 1);
        var removeLine = await admin.SendAsync(Request(HttpMethod.Delete, $"/api/basket/items/{Silver(data)}"), Ct);
        var clear = await admin.SendAsync(Request(HttpMethod.Delete, "/api/basket"), Ct);

        foreach (var response in new[] { put, removeLine, clear })
        {
            var problem = await ProblemAsync(response, HttpStatusCode.Forbidden);
            problem.GetProperty("type").GetString().ShouldBe("basket.demo_admin_readonly");
            problem.GetProperty("title").GetString()
                .ShouldBe("The shared demo admin can't use the cart. Try the demo customer instead.");
            SetCookie(response).ShouldBeNull();
        }

        (await BasketAsync(await GetAsync(admin))).Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task DemoAdmin_Merge_MovesNothing_KeepsTheVisitorsAnonymousBasketAndCookie()
    {
        var data = await PublishedDatasetAsync();
        using var visitor = factory.CreateClient();
        var cookie = await NewAnonymousBasketAsync(visitor, Silver(data), 2);
        using var admin = Signed(User.CreateDemoAdmin(UniqueEmail(), DateTime.UtcNow));

        var merge = await MergeAsync(admin, cookie);

        (await BasketAsync(merge)).Lines.ShouldBeEmpty();
        SetCookie(merge).ShouldBeNull(); // the cookie is not expired
        (await BasketAsync(await GetAsync(admin))).Lines.ShouldBeEmpty();
        (await BasketAsync(await GetAsync(visitor, cookie))).Lines.ShouldHaveSingleItem().Quantity.ShouldBe(2);
    }

    [Fact]
    public async Task DemoCustomer_AndNonDemoAdmin_CanStillWriteAndMerge()
    {
        var data = await PublishedDatasetAsync();
        using var demoCustomer = Signed(User.CreateDemoCustomer(UniqueEmail(), DateTime.UtcNow));
        var realAdmin = User.CreateDemoAdmin(UniqueEmail(), DateTime.UtcNow);
        typeof(User).GetProperty(nameof(User.IsDemo))!.SetValue(realAdmin, false); // an Admin that is not the demo account
        using var admin = Signed(realAdmin);

        (await BasketAsync(await PutAsync(demoCustomer, Silver(data), 1))).Lines.ShouldHaveSingleItem();
        (await BasketAsync(await PutAsync(admin, Headset(data), 1))).Lines.ShouldHaveSingleItem();

        using var visitor = factory.CreateClient();
        var cookie = await NewAnonymousBasketAsync(visitor, Black(data), 1);
        (await BasketAsync(await MergeAsync(demoCustomer, cookie))).Lines.Count.ShouldBe(2);
    }

    private static Email UniqueEmail() => Email.Create($"basket-{Guid.NewGuid():N}@x.test").Value;

    /// <summary>A client carrying a real access token for <paramref name="user"/>, issued by the host's own issuer.</summary>
    private HttpClient Signed(User user)
    {
        var token = Services.GetRequiredService<ITokenIssuer>().Issue(user, DateTime.UtcNow).Value;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>A correctly signed token for the test host that expired an hour ago.</summary>
    private static string ExpiredToken(Guid userId)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "voltlane",
            Audience = "voltlane-web",
            Subject = new ClaimsIdentity([new Claim("sub", userId.ToString())]),
            IssuedAt = now.AddHours(-2),
            NotBefore = now.AddHours(-2),
            Expires = now.AddHours(-1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ShopApiFactory.TestSigningKey)), SecurityAlgorithms.HmacSha256),
        });
    }
}
