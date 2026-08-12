using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places orders on the existing order model and drives the SMS notifications that follow an order
/// through its lifecycle. Messaging never fails the underlying operation: a failed send is recorded
/// as an outcome and the order is still placed, dispatched or cancelled.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far out the "how did the delivery go?" follow-up is queued with the provider.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly CultureInfo MoneyCulture = CultureInfo.GetCultureInfo("en-US");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsProvider _smsProvider;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsProvider smsProvider,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsProvider = smsProvider;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, PlaceOrderInput input, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(input, nameof(input));

        if (input.Items is null || input.Items.Count == 0)
            throw new OrderInputException("An order must contain at least one item.");
        if (input.Items.Any(i => i.Quantity <= 0))
            throw new OrderInputException("Every item quantity must be greater than zero.");

        var ids = input.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new OrderInputException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = input.Items.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, BuildAddress(input.ShipToAddress), orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        _logger.LogInformation("Placed order {OrderId} for buyer {BuyerId}", order.Id, buyerId);

        await NotifyAllContactsAsync(order, NotificationType.OrderPlaced,
            PlacedBody(order.Id, order.Total()), sendAt: null, cancellationToken);

        return order.Id;
    }

    public async Task<bool> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return false;

        await NotifyAllContactsAsync(order, NotificationType.OrderDispatched,
            DispatchedBody(order.Id), sendAt: null, cancellationToken);

        // Queue the delivery follow-up with the provider itself, for a few days out — the app holds no timer.
        var followUpAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await NotifyAllContactsAsync(order, NotificationType.DeliveryFollowUp,
            FollowUpBody(order.Id), sendAt: followUpAt, cancellationToken);

        return true;
    }

    public async Task<bool> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return false;

        // First, call off any delivery follow-up that is queued with the provider but not yet sent —
        // asking how a cancelled order's delivery went is exactly the incident this prevents.
        var existing = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in existing.Where(IsCancelableFollowUp))
        {
            try
            {
                await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageId!, cancellationToken);
                followUp.UpdateDeliveryStatus(NotificationStatus.Canceled, null, null);
                await _notifications.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Called off scheduled follow-up {NotificationId} for cancelled order {OrderId}",
                    followUp.Id, orderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} for order {OrderId}: {Error}",
                    followUp.Id, orderId, Sanitize(ex.Message, followUp.ToPhoneNumber));
            }
        }

        await NotifyAllContactsAsync(order, NotificationType.OrderCancelled,
            CancelledBody(order.Id), sendAt: null, cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notifications.ListAsync(new NotificationsByBuyerSpecification(buyerId), cancellationToken);

        await RefreshDeliveryOutcomesAsync(notifications, cancellationToken);

        var byOrder = notifications
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.OrderBy(n => n.CreatedDate).ToList());

        return orders
            .Select(o => new OrderWithNotifications(o,
                byOrder.TryGetValue(o.Id, out var list) ? list : Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scope to the caller's own order — another shopper's order is treated as not found.
        var order = await _orders.FirstOrDefaultAsync(new CustomerOrderByIdSpecification(buyerId, orderId), cancellationToken);
        if (order is null)
            return null;

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshDeliveryOutcomesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key sends nothing new and returns the original result.
        var priorForKey = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey is not null)
        {
            _logger.LogInformation("Resend under idempotency key matched existing notification {NotificationId}; no new message sent",
                priorForKey.Id);
            return ResendResult.Replayed(priorForKey.Id);
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
            return ResendResult.NotFound();

        // Recompose the text from the message's type/order so a resend works even after content disposal.
        var body = BodyFor(original.Type, original.OrderId);

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.Type, original.ToPhoneNumber, body);
        resend.MarkResendOf(original.Id, idempotencyKey);
        await SendAndRecordAsync(resend, new SendSmsRequest(original.ToPhoneNumber, body), cancellationToken);
        resend = await _notifications.AddAsync(resend, cancellationToken);

        _logger.LogInformation("Resent notification {OriginalId} as {NotificationId} for order {OrderId}",
            original.Id, resend.Id, original.OrderId);

        return ResendResult.Sent(resend.Id);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            return false;

        // Redact at the provider first, so the text is no longer retrievable there; if this throws we
        // do not claim success. The record of the send and its outcome deliberately survive.
        if (notification.ProviderMessageId is not null)
            await _smsProvider.RedactBodyAsync(notification.ProviderMessageId, cancellationToken);

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);

        _logger.LogInformation("Disposed of content for notification {NotificationId}", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // The provider is asked directly for the configured sending number's messages in the range.
        var providerMessages = await _smsProvider.ListSentMessagesAsync(from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        // eShop's belief: immediate messages (those actually sent from the configured number, not the
        // scheduled follow-ups queued via the messaging service) that got a provider id within the range.
        var allNotifications = await _notifications.ListAsync(cancellationToken);
        var eShopInRange = allNotifications
            .Where(n => n.ProviderMessageId is not null
                        && n.ScheduledSendAt is null
                        && n.CreatedDate >= from
                        && n.CreatedDate <= to)
            .ToList();
        var eShopBySid = eShopInRange
            .GroupBy(n => n.ProviderMessageId!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciledMessage>();
        var providerOnly = new List<ReconciledMessage>();
        var eShopOnly = new List<ReconciledMessage>();

        foreach (var (sid, providerMessage) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var notification))
            {
                matched.Add(new ReconciledMessage
                {
                    ProviderMessageId = sid,
                    NotificationId = notification.Id,
                    OrderId = notification.OrderId,
                    ProviderStatus = providerMessage.Status,
                    EShopStatus = notification.Status,
                    DateSent = providerMessage.DateSent,
                    To = MaskNumber(providerMessage.To ?? notification.ToPhoneNumber)
                });
            }
            else
            {
                providerOnly.Add(new ReconciledMessage
                {
                    ProviderMessageId = sid,
                    ProviderStatus = providerMessage.Status,
                    DateSent = providerMessage.DateSent,
                    To = MaskNumber(providerMessage.To)
                });
            }
        }

        foreach (var (sid, notification) in eShopBySid)
        {
            if (providerBySid.ContainsKey(sid))
                continue;
            eShopOnly.Add(new ReconciledMessage
            {
                ProviderMessageId = sid,
                NotificationId = notification.Id,
                OrderId = notification.OrderId,
                EShopStatus = notification.Status,
                To = MaskNumber(notification.ToPhoneNumber)
            });
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched.OrderBy(m => m.DateSent).ToList(),
            ProviderOnly = providerOnly.OrderBy(m => m.DateSent).ToList(),
            EShopOnly = eShopOnly,
            ProviderMessageCount = providerBySid.Count,
            EShopMessageCount = eShopBySid.Count
        };
    }

    // -- helpers ------------------------------------------------------------------------------------

    /// <summary>
    /// Sends one message to every number the buyer has on file, recording a notification for each.
    /// A buyer with no number is simply not messaged. A failed send is recorded, never thrown.
    /// </summary>
    private async Task NotifyAllContactsAsync(Order order, NotificationType type, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var contacts = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (contacts.Count == 0)
        {
            _logger.LogInformation("No contact number on file for buyer {BuyerId}; order {OrderId} {Type} not messaged",
                order.BuyerId, order.Id, type);
            return;
        }

        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, type, contact.PhoneNumber, body);
            if (sendAt.HasValue)
                notification.MarkScheduled(sendAt.Value);

            await SendAndRecordAsync(notification, new SendSmsRequest(contact.PhoneNumber, body) { SendAt = sendAt }, cancellationToken);
            await _notifications.AddAsync(notification, cancellationToken);
        }
    }

    /// <summary>Hands a message to the provider and records the outcome on the notification. Never throws on send failure.</summary>
    private async Task SendAndRecordAsync(OrderNotification notification, SendSmsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _smsProvider.SendAsync(request, cancellationToken);
            notification.RecordProviderResult(message.Sid, message.Status, message.ErrorCode, message.ErrorMessage);
        }
        catch (Exception ex)
        {
            var safe = Sanitize(ex.Message, notification.ToPhoneNumber);
            notification.RecordSendFailure(safe);
            _logger.LogWarning("SMS send failed for order {OrderId} ({Type}): {Error}",
                notification.OrderId, notification.Type, safe);
        }
    }

    /// <summary>Refreshes non-terminal notifications from the provider so reports show the current outcome.</summary>
    private async Task RefreshDeliveryOutcomesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageId is null || NotificationStatus.IsTerminal(notification.Status))
                continue;

            try
            {
                var message = await _smsProvider.FetchAsync(notification.ProviderMessageId, cancellationToken);
                if (!string.Equals(message.Status, notification.Status, StringComparison.Ordinal)
                    || message.ErrorCode != notification.ErrorCode)
                {
                    notification.UpdateDeliveryStatus(message.Status, message.ErrorCode, message.ErrorMessage);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh delivery outcome for notification {NotificationId}: {Error}",
                    notification.Id, Sanitize(ex.Message, notification.ToPhoneNumber));
            }
        }
    }

    private static bool IsCancelableFollowUp(OrderNotification n) =>
        n.Type == NotificationType.DeliveryFollowUp
        && n.ProviderMessageId is not null
        && !NotificationStatus.IsTerminal(n.Status)
        && !string.Equals(n.Status, NotificationStatus.SendFailed, StringComparison.Ordinal);

    private static Address BuildAddress(ShippingAddressInput? input) => new(
        street: Coalesce(input?.Street),
        city: Coalesce(input?.City),
        state: Coalesce(input?.State),
        country: Coalesce(input?.Country),
        zipcode: Coalesce(input?.ZipCode));

    private static string Coalesce(string? value) => string.IsNullOrWhiteSpace(value) ? "N/A" : value.Trim();

    private string BodyFor(NotificationType type, int orderId) => type switch
    {
        NotificationType.OrderPlaced => $"eShop: Your order #{orderId} has been placed. Thanks for shopping with us!",
        NotificationType.OrderDispatched => DispatchedBody(orderId),
        NotificationType.DeliveryFollowUp => FollowUpBody(orderId),
        NotificationType.OrderCancelled => CancelledBody(orderId),
        _ => $"eShop: An update about your order #{orderId}."
    };

    private static string PlacedBody(int orderId, decimal total) =>
        $"eShop: Thanks! Your order #{orderId} has been placed for {total.ToString("C", MoneyCulture)}. We'll text you as it ships.";

    private static string DispatchedBody(int orderId) =>
        $"eShop: Good news - your order #{orderId} is on its way!";

    private static string FollowUpBody(int orderId) =>
        $"eShop: How did the delivery of order #{orderId} go? We'd love your feedback.";

    private static string CancelledBody(int orderId) =>
        $"eShop: Your order #{orderId} has been cancelled. No charge stands. Contact us with any questions.";

    /// <summary>Masks a destination for report output — first two and last two characters only.</summary>
    private static string? MaskNumber(string? number)
    {
        if (string.IsNullOrEmpty(number))
            return number;
        if (number.Length <= 4)
            return new string('*', number.Length);
        return number[..2] + new string('*', number.Length - 4) + number[^2..];
    }

    /// <summary>Removes a known phone number from free text so it can never reach a log or stored error.</summary>
    private static string Sanitize(string? text, string secret)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return string.IsNullOrEmpty(secret) ? text : text.Replace(secret, "[redacted]");
    }
}
