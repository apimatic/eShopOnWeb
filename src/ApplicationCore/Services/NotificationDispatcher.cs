using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationDispatcher : INotificationDispatcher
{
    private readonly ISmsProvider _provider;
    private readonly IRepository<SmsNotification> _notifications;
    private readonly IAppLogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        ISmsProvider provider,
        IRepository<SmsNotification> notifications,
        IAppLogger<NotificationDispatcher> logger)
    {
        _provider = provider;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<SmsNotification> SendNewAsync(SmsNotification notification, CancellationToken cancellationToken = default)
    {
        await ApplySendAsync(notification, cancellationToken);
        return await _notifications.AddAsync(notification, cancellationToken);
    }

    public async Task RefreshAsync(SmsNotification notification, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageId) || SmsStatusMapper.IsTerminal(notification.Status))
        {
            return;
        }

        try
        {
            var state = await _provider.FetchAsync(notification.ProviderMessageId, cancellationToken);
            if (state is null)
            {
                return;
            }

            notification.UpdateDeliveryState(
                SmsStatusMapper.Map(state.Status), state.Status, state.ErrorCode, state.ErrorMessage, state.DateSent);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            // A failure to refresh must not break a read; report yesterday's state rather than error out.
            _logger.LogWarning($"Could not refresh delivery state for message {notification.ProviderMessageId}: {ex.Message}");
        }
    }

    public async Task RefreshManyAsync(IEnumerable<SmsNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            await RefreshAsync(notification, cancellationToken);
        }
    }

    private async Task ApplySendAsync(SmsNotification notification, CancellationToken cancellationToken)
    {
        // The body is always present at send time; it is only cleared later by content disposal.
        var command = new SmsSendCommand(notification.ToNumber, notification.Body!, notification.ScheduledFor);

        try
        {
            var result = await _provider.SendAsync(command, cancellationToken);
            if (result.Accepted)
            {
                notification.RecordSendResult(
                    result.Sid, SmsStatusMapper.Map(result.Status), result.Status,
                    result.ErrorCode, result.ErrorMessage, result.DateSent);
            }
            else
            {
                notification.RecordSendResult(
                    null, NotificationStatus.SendError, result.Status, result.ErrorCode, result.ErrorMessage);
                _logger.LogWarning(
                    $"Provider declined message for order {notification.OrderId} ({notification.Kind}); error code {result.ErrorCode}.");
            }
        }
        catch (Exception ex)
        {
            // Never let a send failure fail the order operation that triggered it.
            notification.RecordSendResult(null, NotificationStatus.SendError, null, null, "provider unavailable");
            _logger.LogWarning($"Failed to hand message to provider for order {notification.OrderId} ({notification.Kind}): {ex.Message}");
        }
    }
}
