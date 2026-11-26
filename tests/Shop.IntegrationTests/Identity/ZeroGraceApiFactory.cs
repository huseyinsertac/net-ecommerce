using Microsoft.AspNetCore.Hosting;

namespace Shop.IntegrationTests.Identity;

/// <summary>
/// The shared host with the refresh reuse grace switched off, so a replay right after a rotation is treated as theft.
/// (With the default 30 s grace the same replay is benign concurrent use.) Own database container; used by
/// <see cref="RefreshReuseDetectionTests"/> only.
/// </summary>
public sealed class ZeroGraceApiFactory : ShopApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Jwt:RefreshReuseGraceSeconds", "0");
    }
}
