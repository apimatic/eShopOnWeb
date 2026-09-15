using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.PublicApi;

public sealed class ScheduledMessageCancellationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ScheduledMessageCancellationWorker(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<OrderNotificationService>();
                await service.RetryPendingCancellationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // The durable cancellation-pending state is retried on the next tick. Provider
                // exceptions are deliberately not logged because they may contain message PII.
            }
        }
    }
}
