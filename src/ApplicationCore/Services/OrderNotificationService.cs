using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly ITwilioGateway _twilioGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        ITwilioGateway twilioGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _twilioGateway = twilioGateway;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
        => NotifyAsync(order, OrderNotificationKind.OrderPlaced, BuildBody(OrderNotificationKind.OrderPlaced, order.Id), null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await NotifyAsync(order, OrderNotificationKind.OrderDispatched, BuildBody(OrderNotificationKind.OrderDispatched, order.Id), null, cancellationToken);
        await NotifyAsync(order, OrderNotificationKind.DeliveryFollowUp, BuildBody(OrderNotificationKind.DeliveryFollowUp, order.Id), DateTimeOffset.UtcNow.Add(FollowUpDelay), cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await NotifyAsync(order, OrderNotificationKind.OrderCancelled, BuildBody(OrderNotificationKind.OrderCancelled, order.Id), null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _twilioGateway.FetchMessageAsync(notification.ProviderSid, cancellationToken);
                if (snapshot == null)
                {
                    continue;
                }

                notification.ApplyProviderState(
                    snapshot.Status,
                    snapshot.ErrorCode,
                    snapshot.ErrorMessage,
                    notification.BodyRedacted ? string.Empty : snapshot.Body);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}: {Message}", notification.Id, ex.Message);
            }
        }
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            return null;
        }

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencySpecification(notificationId, idempotencyKey), cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var contacts = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(original.BuyerId), cancellationToken);
        var destination = contacts
            .Select(c => c.PhoneNumber)
            .FirstOrDefault(number => string.Equals(number, original.DestinationPhoneNumber, StringComparison.Ordinal))
            ?? contacts.OrderByDescending(c => c.Id).FirstOrDefault()?.PhoneNumber;

        if (destination == null)
        {
            throw new InvalidOperationException("The shopper has no contact number on file, so the message cannot be resent.");
        }

        var body = original.BodyRedacted ? BuildBody(original.Kind, original.OrderId) : original.Body;
        var snapshot = await TrySendAsync(destination, body, null, cancellationToken);
        var resent = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            body,
            destination,
            snapshot?.Sid,
            snapshot?.Status ?? "failed",
            snapshot?.ErrorCode,
            snapshot?.ErrorMessage,
            dateScheduled: null,
            parentNotificationId: original.Id,
            idempotencyKey: idempotencyKey);

        await _notificationRepository.AddAsync(resent, cancellationToken);
        return resent;
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            try
            {
                var snapshot = await _twilioGateway.UpdateMessageAsync(notification.ProviderSid, body: string.Empty, status: null, cancellationToken);
                if (snapshot != null)
                {
                    notification.ApplyProviderState(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, string.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not redact provider content for notification {NotificationId}: {Message}", notification.Id, ex.Message);
            }
        }

        notification.MarkBodyRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _twilioGateway.ListMessagesAsync(from, to, cancellationToken);
        var local = await _notificationRepository.ListAsync(new OrderNotificationsInRangeSpecification(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<NotificationReconciliationItem>();
        var providerOnly = new List<NotificationReconciliationItem>();
        var localOnly = new List<NotificationReconciliationItem>();

        foreach (var (sid, provider) in providerBySid)
        {
            if (localBySid.TryGetValue(sid, out var localNotification))
            {
                matched.Add(new NotificationReconciliationItem(
                    sid,
                    localNotification.Id,
                    localNotification.ProviderStatus,
                    provider.Status,
                    "matched"));
            }
            else
            {
                providerOnly.Add(new NotificationReconciliationItem(
                    sid,
                    null,
                    null,
                    provider.Status,
                    "providerOnly"));
            }
        }

        foreach (var localNotification in local)
        {
            if (string.IsNullOrEmpty(localNotification.ProviderSid) || !providerBySid.ContainsKey(localNotification.ProviderSid))
            {
                localOnly.Add(new NotificationReconciliationItem(
                    localNotification.ProviderSid,
                    localNotification.Id,
                    localNotification.ProviderStatus,
                    null,
                    "localOnly"));
            }
        }

        return new NotificationReconciliationReport(from, to, matched, providerOnly, localOnly);
    }

    private async Task NotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var contacts = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (contacts.Count == 0)
        {
            _logger.LogInformation("Skipping {Kind} SMS for order {OrderId} because the shopper has no contact number on file", kind, order.Id);
            return;
        }

        foreach (var contact in contacts)
        {
            var snapshot = await TrySendAsync(contact.PhoneNumber, body, sendAt, cancellationToken);
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                body,
                contact.PhoneNumber,
                snapshot?.Sid,
                snapshot?.Status ?? "failed",
                snapshot?.ErrorCode,
                snapshot?.ErrorMessage,
                sendAt,
                parentNotificationId: null,
                idempotencyKey: null);

            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notificationRepository.ListAsync(new PendingFollowUpNotificationsSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _twilioGateway.UpdateMessageAsync(followUp.ProviderSid, body: null, status: "canceled", cancellationToken);
                if (snapshot != null)
                {
                    followUp.ApplyProviderState(
                        snapshot.Status,
                        snapshot.ErrorCode,
                        snapshot.ErrorMessage,
                        followUp.BodyRedacted ? string.Empty : snapshot.Body);
                    await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not cancel follow-up notification {NotificationId} for order {OrderId}: {Message}", followUp.Id, orderId, ex.Message);
            }
        }
    }

    private async Task<TwilioMessageSnapshot?> TrySendAsync(string destination, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            return await _twilioGateway.SendSmsAsync(new SendSmsRequest(destination, body, sendAt), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SMS send failed: {Message}", ex.Message);
            return null;
        }
    }

    private static string BuildBody(OrderNotificationKind kind, int orderId) => kind switch
    {
        OrderNotificationKind.OrderPlaced => $"eShopOnWeb: Your order #{orderId} has been placed. Thank you for shopping with us.",
        OrderNotificationKind.OrderDispatched => $"eShopOnWeb: Order #{orderId} has been dispatched and is on its way.",
        OrderNotificationKind.DeliveryFollowUp => $"eShopOnWeb: How did the delivery of order #{orderId} go? We would love your feedback.",
        OrderNotificationKind.OrderCancelled => $"eShopOnWeb: Order #{orderId} has been cancelled.",
        _ => $"eShopOnWeb: An update is available for order #{orderId}."
    };
}
