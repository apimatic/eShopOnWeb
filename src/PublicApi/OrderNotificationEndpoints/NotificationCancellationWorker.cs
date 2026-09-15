using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class NotificationCancellationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public NotificationCancellationWorker(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<OrderNotificationCoordinator>();
                await coordinator.RetryPendingCancellationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // The next pass retries. Avoid logging exceptions because lookup URLs can contain contact numbers.
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }
}
