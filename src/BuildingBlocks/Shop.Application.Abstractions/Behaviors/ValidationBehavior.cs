using FluentValidation;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Shop.Application.Abstractions.Behaviors;

internal sealed class ValidationBehavior<TRequest, TResult>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResult> where TResult : Result
{
    public async Task<TResult> Handle(TRequest request, HandlerDelegate<TResult> next, CancellationToken ct)
    {
        var validatorList = validators.ToArray();
        if (validatorList.Length == 0)
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validatorList.Select(v => v.ValidateAsync(context, ct)));
        var failures = results.SelectMany(r => r.Errors).ToList();
        if (failures.Count == 0)
        {
            return await next();
        }

        var errors = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray());
        return ResultFactory.Failure<TResult>(new ValidationError(errors));
    }
}
