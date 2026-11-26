namespace Identity.Application.Abstractions;

/// <summary>Refresh-token timing rules (configuration lives in Infrastructure).</summary>
public interface IRefreshTokenPolicy
{
    /// <summary>How long a freshly issued refresh token stays valid.</summary>
    TimeSpan Lifetime { get; }

    /// <summary>How long a just-rotated token may be presented again without being treated as theft.</summary>
    TimeSpan ReuseGrace { get; }
}
