using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Identity.Application.Abstractions;

namespace Identity.Infrastructure.Security;

/// <summary>32 random bytes as base64url for the client; only the lowercase-hex SHA-256 of that string is stored.</summary>
internal sealed class RefreshTokenFactory : IRefreshTokenFactory
{
    private const int TokenBytes = 32;

    public (string Raw, string Hash) Create()
    {
        var raw = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        return (raw, Hash(raw));
    }

    public string Hash(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
