using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

/// <summary>
/// There is no publicly reachable URL for this application, so the provider cannot
/// call back into it. Delivery outcomes are obtained by asking the provider; this
/// refreshes the stored status of any notification that is not yet in a terminal state.
/// </summary>
public class NotificationStatusRefresher
{
    private static readonly HashSet<string> TerminalStatuses = new()
    {
        "delivered", "undelivered", "failed", "canceled", "error", "cancel-failed"
    };

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly ILogger<NotificationStatusRefresher> _logger;

    public NotificationStatusRefresher(
        IRepository<OrderNotification> notificationRepository,
        ISmsProvider smsProvider,
        ILogger<NotificationStatusRefresher> logger)
    {
        _notificationRepository = notificationRepository;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || TerminalStatuses.Contains(notification.Status))
            {
                continue;
            }
            try
            {
                var result = await _smsProvider.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
                if (result is not null && result.Status is not null && result.Status != notification.Status)
                {
                    notification.UpdateProviderStatus(result.Status, result.ErrorCode, result.ErrorMessage);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "Could not refresh status for notification {NotificationId}", notification.Id);
            }
        }
    }
}
