namespace Identity.Application.Abstractions;

/// <summary>How long a freshly issued refresh token stays valid (configuration lives in Infrastructure).</summary>
public interface IRefreshTokenPolicy
{
    TimeSpan Lifetime { get; }
}
