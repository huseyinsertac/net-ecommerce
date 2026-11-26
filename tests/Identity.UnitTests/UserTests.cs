using Identity.Domain.Users;
using Shouldly;

namespace Identity.UnitTests;

public class UserTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private static Email E(string raw = "jane@example.com") => Email.Create(raw).Value;

    private static User NewUser() => User.Register(E(), "Jane", "hash", Now).Value;

    [Fact]
    public void Email_Create_TrimsAndNormalizes()
    {
        var email = Email.Create("  Jane@Example.com ").Value;

        email.Value.ShouldBe("Jane@Example.com");
        email.Normalized.ShouldBe("JANE@EXAMPLE.COM");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("a@")]
    [InlineData("@b")]
    [InlineData("a@b@c")]
    [InlineData(null)]
    public void Email_Create_Invalid_Fails(string? raw)
    {
        Email.Create(raw).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Email_Create_TooLong_Fails()
    {
        var raw = new string('a', 255 - 2) + "@b";
        raw.Length.ShouldBe(255);

        Email.Create(raw).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Register_AssignsCustomerRole()
    {
        var user = User.Register(E(), "  Jane ", "hash", Now).Value;

        user.Roles.ShouldBe([Roles.Customer]);
        user.DisplayName.ShouldBe("Jane");
        user.IsDemo.ShouldBeFalse();
        user.CanLogInWithPassword.ShouldBeTrue();
        user.CreatedAtUtc.ShouldBe(Now);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_InvalidDisplayName_Fails(string name)
    {
        var result = User.Register(E(), name, "hash", Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("identity.invalid_display_name");
    }

    [Fact]
    public void Register_TooLongDisplayName_Fails()
    {
        User.Register(E(), new string('x', 81), "hash", Now).Error.Code.ShouldBe("identity.invalid_display_name");
    }

    [Fact]
    public void DemoCustomer_CannotLogInWithPassword()
    {
        var user = User.CreateDemoCustomer(E("demo-1@demo.voltlane.test"), Now);

        user.IsDemo.ShouldBeTrue();
        user.PasswordHash.ShouldBeNull();
        user.CanLogInWithPassword.ShouldBeFalse();
        user.Roles.ShouldBe([Roles.Customer]);
    }

    [Fact]
    public void DemoAdmin_HasAdminAndCustomerRoles()
    {
        var user = User.CreateDemoAdmin(E("admin@demo.voltlane.test"), Now);

        user.IsDemo.ShouldBeTrue();
        user.CanLogInWithPassword.ShouldBeFalse();
        user.Roles.ShouldContain(Roles.Admin);
        user.Roles.ShouldContain(Roles.Customer);
    }

    [Fact]
    public void Rotate_ActiveToken_ReturnsNewTokenInSameFamily_AndRevokesOld()
    {
        var user = NewUser();
        var first = user.IssueRefreshToken("h1", Now, Lifetime);
        var later = Now.AddMinutes(5);

        var result = user.Rotate("h1", "h2", later, Lifetime);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FamilyId.ShouldBe(first.FamilyId);
        result.Value.TokenHash.ShouldBe("h2");
        result.Value.IsActive(later).ShouldBeTrue();
        first.RevokedAtUtc.ShouldBe(later);
        first.ReplacedById.ShouldBe(result.Value.Id);
        first.IsActive(later).ShouldBeFalse();
    }

    [Fact]
    public void Rotate_RevokedToken_RevokesWholeFamily_AndFails()
    {
        var user = NewUser();
        var first = user.IssueRefreshToken("h1", Now, Lifetime);
        var second = user.Rotate("h1", "h2", Now.AddMinutes(1), Lifetime).Value;
        var replayAt = Now.AddMinutes(2);

        var replay = user.Rotate("h1", "h3", replayAt, Lifetime);

        replay.IsFailure.ShouldBeTrue();
        replay.Error.Code.ShouldBe("identity.invalid_refresh_token");
        var family = user.RefreshTokens.Where(t => t.FamilyId == first.FamilyId).ToList();
        family.Count.ShouldBe(2);
        family.ShouldAllBe(t => t.RevokedAtUtc != null);
        second.IsActive(replayAt).ShouldBeFalse();
    }

    [Fact]
    public void Rotate_RecentlyRotatedTokenWithinGrace_IssuesNewTokenSameFamily_NoFamilyRevoke()
    {
        var user = NewUser();
        var first = user.IssueRefreshToken("h1", Now, Lifetime);
        var second = user.Rotate("h1", "h2", Now.AddMinutes(1), Lifetime).Value;
        var replayAt = Now.AddMinutes(1).AddSeconds(30);

        var concurrent = user.Rotate("h1", "h3", replayAt, Lifetime);

        concurrent.IsSuccess.ShouldBeTrue();
        concurrent.Value.FamilyId.ShouldBe(first.FamilyId);
        concurrent.Value.IsActive(replayAt).ShouldBeTrue();
        second.IsActive(replayAt).ShouldBeTrue();
    }

    [Fact]
    public void Rotate_RecentlyRotatedTokenAfterGrace_RevokesFamily()
    {
        var user = NewUser();
        var first = user.IssueRefreshToken("h1", Now, Lifetime);
        var second = user.Rotate("h1", "h2", Now.AddMinutes(1), Lifetime).Value;
        var replayAt = Now.AddMinutes(1).AddSeconds(31);

        var replay = user.Rotate("h1", "h3", replayAt, Lifetime);

        replay.IsFailure.ShouldBeTrue();
        second.IsActive(replayAt).ShouldBeFalse();
        user.RefreshTokens.Where(t => t.FamilyId == first.FamilyId).ShouldAllBe(t => t.RevokedAtUtc != null);
    }

    [Fact]
    public void Rotate_RotatedTokenWithinGrace_ButFamilyRevokedByLogout_Fails()
    {
        var user = NewUser();
        var first = user.IssueRefreshToken("h1", Now, Lifetime);
        user.Rotate("h1", "h2", Now.AddMinutes(1), Lifetime);
        user.RevokeFamily(first.FamilyId, Now.AddMinutes(1).AddSeconds(5));

        var replay = user.Rotate("h1", "h3", Now.AddMinutes(1).AddSeconds(10), Lifetime);

        replay.IsFailure.ShouldBeTrue();
        user.RefreshTokens.ShouldAllBe(t => t.RevokedAtUtc != null);
    }

    [Fact]
    public void Rotate_ExpiredToken_Fails()
    {
        var user = NewUser();
        user.IssueRefreshToken("h1", Now, Lifetime);

        var result = user.Rotate("h1", "h2", Now.AddDays(8), Lifetime);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("identity.invalid_refresh_token");
    }

    [Fact]
    public void Rotate_UnknownHash_Fails()
    {
        var user = NewUser();
        user.IssueRefreshToken("h1", Now, Lifetime);

        var result = user.Rotate("nope", "h2", Now, Lifetime);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("identity.invalid_refresh_token");
    }

    [Fact]
    public void RevokeAll_RevokesEveryActiveToken()
    {
        var user = NewUser();
        user.IssueRefreshToken("h1", Now, Lifetime);
        user.IssueRefreshToken("h2", Now, Lifetime);
        var at = Now.AddHours(1);

        user.RevokeAll(at);

        user.RefreshTokens.ShouldAllBe(t => t.RevokedAtUtc == at);
    }

    [Fact]
    public void IssueRefreshToken_PrunesTokensExpiredMoreThanADayAgo()
    {
        var user = NewUser();
        user.IssueRefreshToken("old", Now, TimeSpan.FromHours(1));

        user.IssueRefreshToken("new", Now.AddDays(3), Lifetime);

        user.RefreshTokens.Select(t => t.TokenHash).ShouldBe(["new"]);
    }
}
