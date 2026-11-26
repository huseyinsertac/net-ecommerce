namespace Identity.Application.Abstractions;

/// <summary>Creates opaque refresh tokens. Only the hash is ever stored; the raw value goes to the client.</summary>
public interface IRefreshTokenFactory
{
    (string Raw, string Hash) Create();

    string Hash(string raw);
}
