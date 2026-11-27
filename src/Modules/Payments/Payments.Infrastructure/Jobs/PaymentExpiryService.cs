using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Application.Abstractions;
using Payments.Application.Payments;
using Payments.Infrastructure.Gateways;
using Shop.Application.Abstractions.Messaging;

namespace Payments.Infrastructure.Jobs;

/// <summary>
/// Every <c>Payments:ExpiryScanInterval</c>, ends the Pending payments whose timeout has passed (see
/// <see cref="ResolveOverduePayment"/>). Each payment runs in its own DI scope, so one failure (a gateway error, a
/// concurrent webhook winning the row) is logged and retried on the next scan without affecting the others.
/// </summary>
internal sealed class PaymentExpiryService(
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentsOptions> options,
    TimeProvider clock,
    ILogger<PaymentExpiryService> logger) : BackgroundService
{
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.ExpiryScanInterval, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await ScanAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Payment expiry scan failed; retrying on the next tick.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // host shutting down
        }
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> overdue;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            overdue = await scope.ServiceProvider.GetRequiredService<IPaymentRepository>()
                .GetOverduePendingIdsAsync(clock.GetUtcNow().UtcDateTime, BatchSize, ct);
        }

        foreach (var paymentId in overdue)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
                    .Send(new ResolveOverduePayment(paymentId), ct);
                if (result.IsFailure)
                {
                    logger.LogWarning("Could not end overdue payment {PaymentId}: {Error}", paymentId, result.Error.Code);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Ending overdue payment {PaymentId} failed; retrying on the next scan.", paymentId);
            }
        }
    }
}
