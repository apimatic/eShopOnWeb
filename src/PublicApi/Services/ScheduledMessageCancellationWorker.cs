using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.PublicApi.Services;

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
        do
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IOrderNotificationService>();
                await service.CancelOutstandingScheduledMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Retry on the next interval. No contact details or provider exception text are logged.
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
