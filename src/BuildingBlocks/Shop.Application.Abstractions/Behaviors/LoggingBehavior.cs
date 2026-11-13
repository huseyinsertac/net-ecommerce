using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Shop.Application.Abstractions.Behaviors;

internal sealed class LoggingBehavior<TRequest, TResult>(ILogger<LoggingBehavior<TRequest, TResult>> logger)
    : IPipelineBehavior<TRequest, TResult> where TResult : Result
{
    public async Task<TResult> Handle(TRequest request, HandlerDelegate<TResult> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        var start = Stopwatch.GetTimestamp();
        var result = await next();
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        if (result.IsFailure)
        {
            logger.LogWarning("{Request} failed with {ErrorCode} in {ElapsedMs} ms", name, result.Error.Code, elapsed);
        }
        else
        {
            logger.LogInformation("{Request} succeeded in {ElapsedMs} ms", name, elapsed);
        }

        return result;
    }
}
