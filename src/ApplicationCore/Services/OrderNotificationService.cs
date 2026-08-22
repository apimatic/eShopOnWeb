using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;
    private readonly ISmsSendingNumber _sendingNumber;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ShopperContactNumber> contactNumbers,
        ISmsGateway smsGateway,
        ISmsSendingNumber sendingNumber,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
        _sendingNumber = sendingNumber;
        _logger = logger;
    }

    public async Task TryNotifyAsync(Order order, OrderNotificationKind kind, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));

        ShopperContactNumber? contact;
        try
        {
            var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(order.BuyerId), cancellationToken);
            contact = numbers.FirstOrDefault();
        }
        catch (Exception)
        {
            _logger.LogWarning("Could not load contact numbers while notifying order {OrderId}.", order.Id);
            return;
        }

        if (contact is null)
        {
            _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; no contact number on file.", kind, order.Id);
            return;
        }

        var body = OrderNotificationTemplates.For(kind, order.Id);
        var record = new OrderNotification(order.Id, order.BuyerId, kind, contact.Id, body);
        record = await _notifications.AddAsync(record, cancellationToken);

        try
        {
            var result = await _smsGateway.SendAsync(new SmsSendRequest(contact.CanonicalPhoneNumber, body, sendAt), cancellationToken);
            record.AttachProviderResult(result.ProviderMessageSid, result.Status, result.ErrorCode, result.ErrorMessage, sendAt);
            if (!result.Accepted)
            {
                record.RecordLocalFailure(result.ErrorMessage ?? "Provider rejected the message.");
                _logger.LogWarning("Provider did not accept {Kind} notification {NotificationId} for order {OrderId}.", kind, record.Id, order.Id);
            }
        }
        catch (Exception)
        {
            record.RecordLocalFailure("Provider send failed.");
            _logger.LogWarning("Failed to send {Kind} notification {NotificationId} for order {OrderId}.", kind, record.Id, order.Id);
        }

        await _notifications.UpdateAsync(record, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpec(orderId), cancellationToken);
        var owned = notifications.Where(n => n.BuyerId == buyerId).ToList();
        await RefreshAsync(owned, cancellationToken);
        return owned;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByBuyerSpec(buyerId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpNotificationsSpec(orderId), cancellationToken);
        await CancelPendingAsync(pending, cancellationToken);
    }

    public async Task CancelPendingForContactAsync(int contactNumberId, CancellationToken cancellationToken = default)
    {
        var pending = await _notifications.ListAsync(new PendingNotificationsByContactSpec(contactNumberId), cancellationToken);
        await CancelPendingAsync(pending, cancellationToken);
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existingResend = await _notifications.FirstOrDefaultAsync(
            new ResendByIdempotencySpec(notificationId, idempotencyKey), cancellationToken);
        if (existingResend is not null)
        {
            return existingResend;
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                     ?? throw new NotificationNotFoundException(notificationId);

        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(source.BuyerId), cancellationToken);
        var contact = numbers.FirstOrDefault();
        if (contact is null)
        {
            throw new InvalidContactNumberException("The shopper has no contact number on file.");
        }

        var body = source.ResolveBodyForResend();
        var record = new OrderNotification(source.OrderId, source.BuyerId, source.Kind, contact.Id, body);
        record.MarkAsResendOf(source.Id, idempotencyKey);
        record = await _notifications.AddAsync(record, cancellationToken);

        try
        {
            var result = await _smsGateway.SendAsync(new SmsSendRequest(contact.CanonicalPhoneNumber, body), cancellationToken);
            record.AttachProviderResult(result.ProviderMessageSid, result.Status, result.ErrorCode, result.ErrorMessage, null);
            if (!result.Accepted)
            {
                record.RecordLocalFailure(result.ErrorMessage ?? "Provider rejected the message.");
            }
        }
        catch (Exception)
        {
            record.RecordLocalFailure("Provider send failed.");
            _logger.LogWarning("Resend of notification {NotificationId} failed.", notificationId);
        }

        await _notifications.UpdateAsync(record, cancellationToken);
        return record;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                           ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var snapshot = await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
            }
            catch (Exception)
            {
                _logger.LogWarning("Provider content disposal failed for notification {NotificationId}.", notificationId);
                throw;
            }
        }

        notification.RedactLocalContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromNumber = _sendingNumber.FromNumber;
        var providerMessages = await _smsGateway.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsInRangeSpec(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationItem>();
        var providerOnly = new List<ReconciliationItem>();
        var seenSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in providerMessages)
        {
            seenSids.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(ToItem(message, notification, "matched"));
            }
            else
            {
                providerOnly.Add(ToItem(message, null, "provider-only"));
            }
        }

        var eshopOnly = local
            .Where(n => string.IsNullOrEmpty(n.ProviderMessageSid) || !seenSids.Contains(n.ProviderMessageSid))
            .Select(n => new ReconciliationItem
            {
                ProviderMessageSid = n.ProviderMessageSid,
                NotificationId = n.Id,
                OrderId = n.OrderId,
                ProviderStatus = n.ProviderStatus,
                DateCreated = n.CreatedAt.ToString("o"),
                Match = "eshop-only"
            })
            .ToList();

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = fromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            EshopOnly = eshopOnly
        };
    }

    private async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot is null)
                {
                    continue;
                }

                notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private async Task CancelPendingAsync(IEnumerable<OrderNotification> pending, CancellationToken cancellationToken)
    {
        foreach (var notification in pending)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.CancelScheduledAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
                _logger.LogInformation("Cancelled pending provider message for notification {NotificationId}.", notification.Id);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not cancel pending provider message for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private static ReconciliationItem ToItem(SmsMessageSnapshot message, OrderNotification? notification, string match) =>
        new()
        {
            ProviderMessageSid = message.Sid,
            NotificationId = notification?.Id,
            OrderId = notification?.OrderId,
            ProviderStatus = message.Status,
            DateSent = message.DateSent,
            DateCreated = message.DateCreated,
            Direction = message.Direction,
            Match = match
        };
}
