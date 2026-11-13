using System.Collections.Concurrent;
using System.Reflection;
using Shop.SharedKernel;

namespace Shop.Application.Abstractions;

internal static class ResultFactory
{
    private static readonly MethodInfo GenericFailure = typeof(Result)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(Result.Failure) && m.IsGenericMethodDefinition);

    private static readonly ConcurrentDictionary<Type, Func<Error, Result>> Cache = new();

    internal static TResult Failure<TResult>(Error error) where TResult : Result
    {
        if (typeof(TResult) == typeof(Result))
        {
            return (TResult)Result.Failure(error);
        }

        var factory = Cache.GetOrAdd(typeof(TResult), static type =>
        {
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Result<>))
            {
                throw new InvalidOperationException($"Unsupported result type {type.Name}.");
            }

            var method = GenericFailure.MakeGenericMethod(type.GetGenericArguments()[0]);
            return e => (Result)method.Invoke(null, [e])!;
        });

        return (TResult)factory(error);
    }
}
