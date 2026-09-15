using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class NotificationCancellationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public NotificationCancellationWorker(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var workflow = scope.ServiceProvider.GetRequiredService<NotificationWorkflowService>();
                await workflow.RetryPendingCancellationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Cancellation requests remain durable and are retried on the next pass.
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
