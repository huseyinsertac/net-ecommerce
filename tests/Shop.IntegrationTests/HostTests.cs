using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Npgsql;
using Shop.Infrastructure.Common.Http;
using Shouldly;

namespace Shop.IntegrationTests;

[Collection(ShopApiCollection.Name)]
public sealed class HostTests(ShopApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task SystemInfo_ReturnsNameAndVersion()
    {
        var client = factory.CreateClient();
        var info = await client.GetFromJsonAsync<JsonElement>("/api/system/info", Ct);

        info.GetProperty("name").GetString().ShouldBe("VoltLane");
        info.GetProperty("version").GetString().ShouldNotBeNullOrWhiteSpace();
        info.GetProperty("environment").GetString().ShouldBe("Production");
    }

    [Fact]
    public async Task FreshDatabase_AllModuleSchemasMigrated()
    {
        using var _ = factory.CreateClient(); // forces host start (and startup migration)

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT table_schema || '.' || table_name FROM information_schema.tables", connection);
        var tables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                tables.Add(reader.GetString(0));
            }
        }

        foreach (var schema in new[] { "catalog", "basket", "ordering", "payments", "identity" })
        {
            tables.ShouldContain($"{schema}.outbox_messages");
            tables.ShouldContain($"{schema}.inbox_messages");
            tables.ShouldContain($"{schema}.__ef_migrations_history");
        }
    }

    [Fact]
    public async Task UnhandledException_InProduction_ReturnsGenericProblem()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/test/throw", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadAsStringAsync(Ct);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("title").GetString().ShouldBe("An unexpected error occurred.");
        json.RootElement.TryGetProperty("traceId", out _).ShouldBeTrue();
        body.ShouldNotContain("secret detail");
        body.ShouldNotContain("   at ");
    }

    [Fact]
    public async Task UnknownApiRoute_ReturnsProblemDetails404()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/does-not-exist", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task OpenApi_BehindProxy_UsesForwardedScheme()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");
        request.Headers.Host = "shop.example.com";
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        var url = doc.GetProperty("servers")[0].GetProperty("url").GetString();
        url.ShouldNotBeNull().ShouldStartWith("https://shop.example.com");
    }

    [Theory]
    [InlineData("POST", "/api/identity/refresh")]
    [InlineData("POST", "/api/identity/logout")]
    [InlineData("POST", "/api/identity/demo/customer")]
    [InlineData("POST", "/api/identity/demo/admin")]
    [InlineData("POST", "/api/identity/login")]
    [InlineData("POST", "/api/identity/register")]
    [InlineData("POST", "/api/basket/merge")]
    [InlineData("PUT", "/api/basket/items/0190b2b4-0000-7000-8000-000000000001")]
    [InlineData("DELETE", "/api/basket/items/0190b2b4-0000-7000-8000-000000000001")]
    [InlineData("DELETE", "/api/basket")]
    public async Task NonGetApiRequest_WithoutCsrfHeader_400MissingCsrfHeader(string method, string path)
    {
        using var client = WithoutCsrfHeader();
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { quantity = 1, email = "a@b.test", password = "x", displayName = "x" }),
        };

        var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("type").GetString().ShouldBe("request.missing_csrf_header");
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
    }

    [Fact]
    public async Task Post_WithWrongCsrfHeaderValue_400()
    {
        using var client = WithoutCsrfHeader();
        client.DefaultRequestHeaders.Add(ShopApiFactory.CsrfHeaderName, "XMLHttpRequest");

        var response = await client.PostAsync("/api/identity/demo/customer", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_WithoutCsrfHeader_StillWorks()
    {
        using var client = WithoutCsrfHeader();

        (await client.GetAsync("/api/basket", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/system/info", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_WithCsrfHeader_ReachesTheEndpoint()
    {
        using var client = factory.CreateClient(); // the factory adds the header

        var response = await client.PostAsync("/api/identity/logout", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CsrfExemptPath_IsNotChecked()
    {
        CsrfHeader.ExemptPaths.ShouldBe(["/api/payments/stripe/webhook"]);
        using var client = WithoutCsrfHeader();

        // Stripe cannot send the header. The test host runs the Fake provider, so the webhook is not mapped: a 404 (not the
        // CSRF 400) shows the request got past the check.
        var response = await client.PostAsync("/api/payments/stripe/webhook", new StringContent("{}"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/API/Payments/Stripe/Webhook";
        CsrfHeader.AppliesTo(context.Request).ShouldBeFalse();
        context.Request.Path = "/api/payments/stripe/webhook/other";
        CsrfHeader.AppliesTo(context.Request).ShouldBeTrue();
    }

    private HttpClient WithoutCsrfHeader()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove(ShopApiFactory.CsrfHeaderName);
        return client;
    }

    [Fact]
    public async Task Scalar_IsServed()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/scalar", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
