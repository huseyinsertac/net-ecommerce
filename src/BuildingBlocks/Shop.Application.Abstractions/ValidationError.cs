using Shop.SharedKernel;

namespace Shop.Application.Abstractions;

public sealed record ValidationError(IReadOnlyDictionary<string, string[]> Errors)
    : Error("validation.failed", "One or more validation errors occurred.", ErrorType.Validation);
