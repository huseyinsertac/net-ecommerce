using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;

namespace Shop.IntegrationTests;

[Collection(ShopApiCollection.Name)]
public sealed class OpenApiSnapshotTests(ShopApiFactory factory)
{
    private const string UpdateVariable = "UPDATE_OPENAPI_SNAPSHOT";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [Fact]
    public async Task OpenApiDocument_MatchesCommittedSnapshot()
    {
        using var client = factory.CreateClient();
        var raw = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        var document = JsonNode.Parse(raw)!.AsObject();
        document.Remove("servers");
        var actual = document.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";

        var path = Path.Combine(RepoPaths.Root, "web", "shop-web", "openapi", "v1.json");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, actual, TestContext.Current.CancellationToken);
            return;
        }

        File.Exists(path).ShouldBeTrue($"Missing {path}: run with {UpdateVariable}=1 to create it");
        var expected = (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");

        actual.ShouldBe(expected, $"API contract changed: run with {UpdateVariable}=1 and regenerate frontend types");
    }
}
