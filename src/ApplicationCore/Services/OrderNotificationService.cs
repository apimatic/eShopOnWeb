using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates order placement and lifecycle together with the SMS messages that go out as an order moves.
///
/// Two invariants run through it:
///  * A message that cannot be sent never fails the underlying order operation — the order is still placed,
///    dispatched or cancelled, and a failed send is recorded as an outcome, not raised.
///  * A shopper's number is never written to logs. Log lines carry order ids, notification ids, statuses and
///    provider message SIDs — never the destination number or provider failure text.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // How far in the future the "how did the delivery go?" follow-up is queued at dispatch.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ITwilioMessagingService _messaging;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ITwilioMessagingService messaging,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _messaging = messaging;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string rawNumber, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(rawNumber, nameof(rawNumber));

        // Reject an unusable destination now, at registration — not when a later message fails to send.
        var validation = await _messaging.ValidateNumberAsync(rawNumber, ct);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            throw new SmsGatewayException(
                $"The number is not a usable destination. {validation.FailureReason}".Trim(),
                System.Net.HttpStatusCode.BadRequest);
        }

        // Store the provider's canonical form, and don't register the same number twice for one shopper.
        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (already is not null)
        {
            return already;
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        contactNumber = await _contactNumberRepository.AddAsync(contactNumber, ct);
        _logger.LogInformation("Registered contact number {ContactNumberId} for a shopper.", contactNumber.Id);
        return contactNumber;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new ArgumentException("Every item quantity must be greater than zero.", nameof(lines));
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), ct);

        var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", nameof(lines));
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        order = await _orderRepository.AddAsync(order, ct);
        _logger.LogInformation("Placed order {OrderId} for a shopper.", order.Id);

        var body = $"eShop: thanks! Your order #{order.Id} has been placed.";
        await NotifyAllNumbersAsync(order, NotificationType.OrderPlaced, body, ct);

        return order;
    }

    public async Task DispatchOrderAsync(Order order, CancellationToken ct = default)
    {
        Guard.Against.Null(order, nameof(order));

        order.Dispatch();
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Dispatched order {OrderId}.", order.Id);

        var numbers = await GetBuyerNumbersAsync(order.BuyerId, ct);
        var dispatchedBody = $"eShop: good news — your order #{order.Id} is on its way!";
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);

        foreach (var number in numbers)
        {
            // Tell them it's on its way now...
            await SendAndRecordAsync(order, NotificationType.OrderDispatched, number.PhoneNumber, dispatchedBody, ct);
            // ...and queue the delivery-feedback follow-up WITH THE PROVIDER for a few days later.
            await ScheduleAndRecordAsync(order, number.PhoneNumber, followUpBody, sendAt, ct);
        }
    }

    public async Task CancelOrderAsync(Order order, CancellationToken ct = default)
    {
        Guard.Against.Null(order, nameof(order));

        order.Cancel();
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Cancelled order {OrderId}.", order.Id);

        // Call off any queued follow-up FIRST so a "how did delivery go?" for a cancelled order can never go out.
        var scheduled = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderSpecification(order.Id), ct);
        foreach (var followUp in scheduled)
        {
            try
            {
                await _messaging.CancelScheduledAsync(followUp.ProviderMessageSid!, ct);
                followUp.MarkCanceled();
                await _notificationRepository.UpdateAsync(followUp, ct);
                _logger.LogInformation("Called off queued follow-up {NotificationId} for cancelled order {OrderId}.", followUp.Id, order.Id);
            }
            catch (SmsGatewayException ex)
            {
                _logger.LogWarning("Could not call off follow-up {NotificationId} for order {OrderId} (provider status {Status}). It will be revisited by reconciliation.",
                    followUp.Id, order.Id, ex.StatusCode);
            }
        }

        var numbers = await GetBuyerNumbersAsync(order.BuyerId, ct);
        var body = $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
        foreach (var number in numbers)
        {
            await SendAndRecordAsync(order, NotificationType.OrderCancelled, number.PhoneNumber, body, ct);
        }
    }

    public async Task<OrderNotification> ResendAsync(OrderNotification notification, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.Null(notification, nameof(notification));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns what the first attempt produced — no second send.
        var priorForKey = await _notificationRepository.FirstOrDefaultAsync(new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (priorForKey is not null)
        {
            _logger.LogInformation("Resend under idempotency key already satisfied by notification {NotificationId}.", priorForKey.Id);
            return priorForKey;
        }

        if (!NotificationStatuses.IsUndeliveredOutcome(notification.Status))
        {
            throw new OrderStatusException($"Notification {notification.Id} did not fail to reach the shopper (status '{notification.Status}'); there is nothing to resend.");
        }
        if (notification.ContentRedacted || string.IsNullOrEmpty(notification.Body))
        {
            throw new OrderStatusException($"Notification {notification.Id} has had its content disposed of and cannot be resent.");
        }

        var resend = new OrderNotification(
            notification.OrderId,
            notification.BuyerId,
            notification.Type,
            notification.ToPhoneNumber,
            notification.Body!,
            idempotencyKey: idempotencyKey);

        await TrySendIntoAsync(resend, ct);
        resend = await _notificationRepository.AddAsync(resend, ct);
        _logger.LogInformation("Resent notification {OriginalId} as {NotificationId} (status {Status}).", notification.Id, resend.Id, resend.Status);
        return resend;
    }

    public async Task DisposeContentAsync(OrderNotification notification, CancellationToken ct = default)
    {
        Guard.Against.Null(notification, nameof(notification));

        // Dispose of the text at the provider first; only clear it locally once the provider has too. If the
        // provider redaction fails, surface it — the content is NOT gone, and the caller must know that.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _messaging.RedactContentAsync(notification.ProviderMessageSid!, ct);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, ct);
        _logger.LogInformation("Disposed of content for notification {NotificationId} (SID {Sid}); the send record survives.", notification.Id, notification.ProviderMessageSid);
    }

    public async Task RefreshStatusAsync(OrderNotification notification, CancellationToken ct = default)
    {
        Guard.Against.Null(notification, nameof(notification));

        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return; // Never reached the provider — nothing to refresh.
        }
        if (IsTerminal(notification.Status))
        {
            return; // Outcome already settled; don't spend a provider call on it.
        }

        try
        {
            var current = await _messaging.FetchStatusAsync(notification.ProviderMessageSid!, ct);
            if (current.Status != notification.Status || current.ErrorCode.HasValue)
            {
                notification.UpdateDeliveryStatus(current.Status, current.ErrorCode, current.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, ct);
            }
        }
        catch (SmsGatewayException ex)
        {
            // Reporting current status must never itself fail the request.
            _logger.LogWarning("Could not refresh status for notification {NotificationId} (provider status {Status}).", notification.Id, ex.StatusCode);
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        // Ask the provider for only this application's sending number's messages over the range (server-side).
        var providerMessages = await _messaging.ListSentFromConfiguredNumberAsync(from, to, ct);

        // eShop's side: our records of messages that reached the provider in the range.
        var eShopNotifications = await _notificationRepository.ListAsync(new SentNotificationsCreatedBetweenSpecification(from, to), ct);
        var eShopBySid = eShopNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        foreach (var msg in providerMessages)
        {
            if (eShopBySid.TryGetValue(msg.Sid, out var ours))
            {
                matched.Add(new ReconciliationEntry(msg.Sid, msg.Status, ours.Status, ours.OrderId, msg.DateSent));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(msg.Sid, msg.Status, null, null, msg.DateSent));
            }
        }

        var eShopOnly = eShopBySid.Values
            .Where(n => !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEntry(n.ProviderMessageSid!, null, n.Status, n.OrderId, null))
            .ToList();

        _logger.LogInformation("Reconciliation over {From:o}..{To:o}: {Matched} matched, {ProviderOnly} provider-only, {EShopOnly} eShop-only.",
            from, to, matched.Count, providerOnly.Count, eShopOnly.Count);

        return new ReconciliationReport(from, to, _messaging.ConfiguredFromNumber, matched, providerOnly, eShopOnly);
    }

    // ---- helpers ----

    private async Task<IReadOnlyList<ContactNumber>> GetBuyerNumbersAsync(string buyerId, CancellationToken ct) =>
        await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);

    private async Task NotifyAllNumbersAsync(Order order, NotificationType type, string body, CancellationToken ct)
    {
        // A shopper with no number on file is simply not messaged.
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, ct);
        foreach (var number in numbers)
        {
            await SendAndRecordAsync(order, type, number.PhoneNumber, body, ct);
        }
    }

    private async Task<OrderNotification> SendAndRecordAsync(Order order, NotificationType type, string toNumber, string body, CancellationToken ct)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, type, toNumber, body);
        await TrySendIntoAsync(notification, ct);
        return await _notificationRepository.AddAsync(notification, ct);
    }

    private async Task ScheduleAndRecordAsync(Order order, string toNumber, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationType.DeliveryFeedback, toNumber, body, scheduledSendAt: sendAt);
        try
        {
            var result = await _messaging.ScheduleAsync(toNumber, body, sendAt, ct);
            notification.MarkAccepted(result.ProviderMessageSid, result.Status);
            _logger.LogInformation("Queued follow-up for order {OrderId} with the provider (SID {Sid}, status {Status}).", order.Id, result.ProviderMessageSid, result.Status);
        }
        catch (SmsGatewayException ex)
        {
            notification.MarkSendFailed(ex.ProviderErrorCode, ex.Message);
            _logger.LogWarning("Could not queue follow-up for order {OrderId} (provider status {Status}, code {Code}). The dispatch still succeeded.", order.Id, ex.StatusCode, ex.ProviderErrorCode);
        }
        await _notificationRepository.AddAsync(notification, ct);
    }

    /// <summary>
    /// Sends the notification's message and records the outcome on it (accepted or send-failed). Never throws
    /// for a messaging failure — the caller's operation must still succeed.
    /// </summary>
    private async Task TrySendIntoAsync(OrderNotification notification, CancellationToken ct)
    {
        try
        {
            var result = await _messaging.SendAsync(notification.ToPhoneNumber, notification.Body!, ct);
            notification.MarkAccepted(result.ProviderMessageSid, result.Status);
            _logger.LogInformation("Sent {Type} notification for order {OrderId} (SID {Sid}, status {Status}).", notification.Type, notification.OrderId, result.ProviderMessageSid, result.Status);
        }
        catch (SmsGatewayException ex)
        {
            notification.MarkSendFailed(ex.ProviderErrorCode, ex.Message);
            _logger.LogWarning("Could not send {Type} notification for order {OrderId} (provider status {Status}, code {Code}). The order operation still succeeded.", notification.Type, notification.OrderId, ex.StatusCode, ex.ProviderErrorCode);
        }
    }

    private static bool IsTerminal(string status) =>
        status is NotificationStatuses.Failed
            or NotificationStatuses.Undelivered
            or NotificationStatuses.Canceled
            or NotificationStatuses.SendFailed
            or "delivered";
}
