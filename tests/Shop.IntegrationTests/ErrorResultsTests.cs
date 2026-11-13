using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shop.Api.ErrorHandling;
using Shop.Application.Abstractions;
using Shop.SharedKernel;
using Shouldly;

namespace Shop.IntegrationTests;

public sealed class ErrorResultsTests
{
    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.Failure, 500)]
    public void ToProblem_MapsErrorTypeToStatus(ErrorType type, int status)
    {
        var result = new Error("some.code", "Some message", type).ToProblem();

        result.ShouldBeAssignableTo<IStatusCodeHttpResult>()!.StatusCode.ShouldBe(status);
        var details = result.ShouldBeAssignableTo<IValueHttpResult>()!.Value.ShouldBeAssignableTo<ProblemDetails>()!;
        details.Type.ShouldBe("some.code");
        details.Title.ShouldBe("Some message");
    }

    [Fact]
    public void ToProblem_ValidationError_IncludesFieldErrors()
    {
        var error = new ValidationError(new Dictionary<string, string[]> { ["Name"] = ["Name is required."] });

        var result = error.ToProblem();

        var details = result.ShouldBeAssignableTo<IValueHttpResult>()!.Value.ShouldBeOfType<HttpValidationProblemDetails>();
        details.Errors["Name"].ShouldBe(["Name is required."]);
        details.Type.ShouldBe("validation.failed");
    }
}
