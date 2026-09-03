using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class ScheduledNotificationCancellationWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScheduledNotificationCancellationWorker> _logger;

    public ScheduledNotificationCancellationWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<ScheduledNotificationCancellationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingCancellationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The scheduled-notification cancellation worker failed without exposing message content or destinations.");
            }

            await Task.Delay(PollInterval, _timeProvider, stoppingToken);
        }
    }

    private async Task ProcessPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var twilio = scope.ServiceProvider.GetRequiredService<ITwilioMessagingGateway>();
        var pending = await context.OrderNotifications
            .Where(notification => notification.Status == OrderNotificationStatus.CancellationPending &&
                                   notification.ProviderMessageSid != null &&
                                   notification.CancellationCompletedAt == null)
            .OrderBy(notification => notification.CancellationRequestedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var notification in pending)
        {
            try
            {
                var current = await twilio.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                if (CannotStillBeDelivered(current.Status))
                {
                    notification.MarkCanceled(current.Status, _timeProvider.GetUtcNow(), current.DateUpdated);
                }
                else if (HasAlreadyReachedCustomer(current.Status))
                {
                    notification.MarkCancellationFailed(current.Status, _timeProvider.GetUtcNow(), current.DateUpdated);
                    _logger.LogCritical(
                        "Scheduled notification {NotificationId} reached a terminal delivery state before provider cancellation completed.",
                        notification.Id);
                }
                else
                {
                    var canceled = await twilio.CancelAsync(notification.ProviderMessageSid!, cancellationToken);
                    notification.MarkCanceled(canceled.Status, _timeProvider.GetUtcNow(), canceled.DateUpdated);
                }
            }
            catch (TwilioProviderException)
            {
                _logger.LogWarning("Provider cancellation remains pending for scheduled notification {NotificationId}.", notification.Id);
            }

            await context.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static bool CannotStillBeDelivered(string status) =>
        status.Equals("canceled", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("undelivered", StringComparison.OrdinalIgnoreCase);

    private static bool HasAlreadyReachedCustomer(string status) =>
        status.Equals("sent", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("delivered", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("read", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("partially_delivered", StringComparison.OrdinalIgnoreCase);
}
