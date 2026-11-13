using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
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

    [Fact]
    public async Task Scalar_IsServed()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/scalar", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
