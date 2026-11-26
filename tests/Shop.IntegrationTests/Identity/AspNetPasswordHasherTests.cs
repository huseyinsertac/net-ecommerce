using Identity.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using IPasswordHasher = Identity.Application.Abstractions.IPasswordHasher;

namespace Shop.IntegrationTests.Identity;

[Collection(ShopApiCollection.Name)]
public sealed class AspNetPasswordHasherTests(ShopApiFactory factory)
{
    private IPasswordHasher Hasher
    {
        get
        {
            using var _ = factory.CreateClient();
            return factory.Services.GetRequiredService<IPasswordHasher>();
        }
    }

    private static User NewUser() =>
        User.Register(Email.Create($"h-{Guid.NewGuid():N}@x.test").Value, "H", "placeholder", DateTime.UtcNow).Value;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Verify_NullOrEmptyHash_ReturnsFalse(string? hash) =>
        Hasher.Verify(NewUser(), hash!, "Passw0rd-test").ShouldBeFalse();

    [Theory]
    [InlineData("not-base64-!!!")]
    [InlineData("AQAAAA")]
    public void Verify_MalformedHash_ReturnsFalse_NoThrow(string hash) =>
        Hasher.Verify(NewUser(), hash, "Passw0rd-test").ShouldBeFalse();

    [Fact]
    public void Verify_RoundTrip()
    {
        var user = NewUser();
        var hash = Hasher.Hash(user, "Passw0rd-test");
        Hasher.Verify(user, hash, "Passw0rd-test").ShouldBeTrue();
        Hasher.Verify(user, hash, "Other-pass1").ShouldBeFalse();
    }
}
