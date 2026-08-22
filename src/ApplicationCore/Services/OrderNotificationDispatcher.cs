using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.ApplicationCore.Twilio;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationDispatcher : IOrderNotificationDispatcher
{
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "received", "read"
    };

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<OrderNotificationDispatcher> _logger;

    public OrderNotificationDispatcher(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioMessagingClient messagingClient,
        IAppLogger<OrderNotificationDispatcher> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public async Task NotifyAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ContactNumber> destinations;
        try
        {
            destinations = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to load contact numbers while notifying order {OrderId}: {Message}", orderId, ex.Message);
            return;
        }

        if (destinations.Count == 0)
        {
            return;
        }

        foreach (var destination in destinations)
        {
            await SendToDestinationAsync(orderId, buyerId, kind, body, destination, sendAt, sourceNotificationId: null, cancellationToken);
        }
    }

    public async Task<OrderNotification> SendToContactAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        ContactNumber destination,
        int? sourceNotificationId,
        CancellationToken cancellationToken = default)
    {
        return await SendToDestinationAsync(orderId, buyerId, kind, body, destination, sendAt: null, sourceNotificationId, cancellationToken);
    }

    public async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            if (IsTerminal(followUp.ProviderStatus) &&
                !string.Equals(followUp.ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(followUp.ProviderStatus, "queued", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(followUp.ProviderStatus, "accepted", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var updated = await _messagingClient.UpdateMessageAsync(followUp.ProviderMessageSid, body: null, status: "canceled", cancellationToken);
                followUp.ApplyProviderState(updated.Status, updated.ErrorCode, updated.ErrorMessage);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (TwilioApiException ex)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}: {Message}",
                    followUp.Id, orderId, ex.Message);
            }
        }
    }

    public async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || IsTerminal(notification.ProviderStatus))
            {
                continue;
            }

            try
            {
                var current = await _messagingClient.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(current.Status, current.ErrorCode, current.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (TwilioApiException ex)
            {
                _logger.LogWarning(
                    "Failed to refresh provider state for notification {NotificationId}: {Message}",
                    notification.Id, ex.Message);
            }
        }
    }

    private async Task<OrderNotification> SendToDestinationAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        ContactNumber destination,
        DateTimeOffset? sendAt,
        int? sourceNotificationId,
        CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(
            orderId,
            buyerId,
            destination.CanonicalNumber,
            kind,
            body,
            destination.Id,
            sourceNotificationId,
            sendAt);

        await _notifications.AddAsync(notification, cancellationToken);

        try
        {
            var created = await _messagingClient.CreateMessageAsync(new CreateTwilioMessageRequest
            {
                To = destination.CanonicalNumber,
                Body = body,
                SendAt = sendAt
            }, cancellationToken);

            if (string.IsNullOrWhiteSpace(created.Sid))
            {
                notification.RecordLocalFailure("The provider accepted the request without a message identifier.");
            }
            else
            {
                notification.RecordProviderAccepted(created.Sid, created.Status, created.ErrorCode, created.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to send {Kind} notification for order {OrderId}: {Message}",
                kind, orderId, ex.Message);
            notification.RecordLocalFailure("The provider rejected or failed the send.");
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    private static bool IsTerminal(string? status)
    {
        return !string.IsNullOrWhiteSpace(status) && TerminalStatuses.Contains(status);
    }
}
