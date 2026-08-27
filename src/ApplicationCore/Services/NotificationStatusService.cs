using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationStatusService : INotificationStatusService
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<NotificationStatusService> _logger;

    public NotificationStatusService(
        IRepository<OrderNotification> notificationRepository,
        ISmsGateway smsGateway,
        IAppLogger<NotificationStatusService> logger)
    {
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (notification.IsTerminal || notification.ProviderMessageSid is null)
            {
                continue;
            }

            try
            {
                var state = await _smsGateway.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderStatus(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                // A failed refresh must not fail the read; the stored status is still reported.
                _logger.LogWarning("Failed to refresh status for notification {NotificationId}: {Error}", notification.Id, ex.Message);
            }
        }
    }
}
