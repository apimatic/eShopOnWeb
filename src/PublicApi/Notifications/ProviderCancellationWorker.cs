using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

// This worker never sends a notification. It only retries provider-side cancellation
// requests already recorded by a delete/cancel API call, closing transient failure windows.
public sealed class ProviderCancellationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ProviderCancellationWorker(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IOrderNotificationService>();
            try
            {
                await service.RetryPendingCancellationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // The durable cancellation request remains pending for the next pass.
            }
        }
    }
}
