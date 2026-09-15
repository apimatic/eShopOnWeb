using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class ScheduledMessageCancellationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledMessageCancellationWorker> _logger;

    public ScheduledMessageCancellationWorker(IServiceScopeFactory scopeFactory,
        ILogger<ScheduledMessageCancellationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<OrderNotificationService>();
                await service.ProcessPendingCancellationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                _logger.LogWarning("A scheduled-message cancellation retry failed; it will be retried.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
