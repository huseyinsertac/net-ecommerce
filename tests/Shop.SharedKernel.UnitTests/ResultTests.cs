using Shouldly;

namespace Shop.SharedKernel.UnitTests;

public class ResultTests
{
    [Fact] public void Success_HasNoError() => Result.Success().Error.ShouldBe(Error.None);

    [Fact]
    public void Failure_ValueAccess_Throws() =>
        Should.Throw<InvalidOperationException>(() => Result.Failure<int>(Error.NotFound("x", "y")).Value);

    [Fact]
    public void ImplicitFromError_IsFailure()
    {
        Result<int> r = Error.Conflict("c", "m");
        r.IsFailure.ShouldBeTrue();
        r.Error.Type.ShouldBe(ErrorType.Conflict);
    }
}
