using Identity.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Security;

internal sealed class ConfiguredRefreshTokenPolicy(IOptions<JwtOptions> options) : IRefreshTokenPolicy
{
    public TimeSpan Lifetime { get; } = TimeSpan.FromDays(options.Value.RefreshTokenDays);

    public TimeSpan ReuseGrace { get; } = TimeSpan.FromSeconds(options.Value.RefreshReuseGraceSeconds);
}
