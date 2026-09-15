using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IUriComposer _uriComposer;
    private readonly ITwilioMessagingClient _twilio;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IUriComposer uriComposer,
        ITwilioMessagingClient twilio,
        IOptions<TwilioSettings> settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _uriComposer = uriComposer;
        _twilio = twilio;
        _settings = settings.Value;
        _logger = logger;
    }

    // ---- Flow 2: placing / moving orders ----

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress)
    {
        if (lines is null || lines.Count == 0)
        {
            return new PlaceOrderResult(ActionOutcome.BadRequest, 0, "An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            return new PlaceOrderResult(ActionOutcome.BadRequest, 0, "Every item quantity must be greater than zero.");
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(itemIds));
        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return new PlaceOrderResult(ActionOutcome.BadRequest, 0,
                $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order);
        _logger.LogInformation("Placed order id={OrderId} for buyer.", order.Id);

        // Messaging must never fail the placement.
        await SafeSendAsync(order, NotificationKind.OrderPlaced, BuildBody(NotificationKind.OrderPlaced, order.Id), null);

        return new PlaceOrderResult(ActionOutcome.Ok, order.Id, null);
    }

    public async Task<OrderActionResult> DispatchOrderAsync(int orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            return new OrderActionResult(ActionOutcome.NotFound, orderId, string.Empty, "Order not found.");
        }

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOrderStateException ex)
        {
            return new OrderActionResult(ActionOutcome.Conflict, orderId, order.Status.ToString(), ex.Message);
        }

        await _orderRepository.UpdateAsync(order);
        _logger.LogInformation("Dispatched order id={OrderId}.", order.Id);

        // Tell the shopper it is on its way now, and queue the "how did it go?" follow-up with the
        // provider a few days out. Neither send failing may fail the dispatch.
        await SafeSendAsync(order, NotificationKind.OrderDispatched, BuildBody(NotificationKind.OrderDispatched, order.Id), null);

        var sendAt = DateTimeOffset.UtcNow.AddDays(Math.Max(1, _settings.FollowUpDelayDays));
        await SafeSendAsync(order, NotificationKind.DeliveryFollowUp, BuildBody(NotificationKind.DeliveryFollowUp, order.Id), sendAt);

        return new OrderActionResult(ActionOutcome.Ok, order.Id, order.Status.ToString(), null);
    }

    public async Task<OrderActionResult> CancelOrderAsync(int orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            return new OrderActionResult(ActionOutcome.NotFound, orderId, string.Empty, "Order not found.");
        }

        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOrderStateException ex)
        {
            return new OrderActionResult(ActionOutcome.Conflict, orderId, order.Status.ToString(), ex.Message);
        }

        await _orderRepository.UpdateAsync(order);
        _logger.LogInformation("Cancelled order id={OrderId}.", order.Id);

        // Call off any follow-up still scheduled with the provider so a cancelled order never gets
        // a "how did delivery go?" message. This is the incident this must prevent.
        await CancelScheduledFollowUpsAsync(order.Id);

        await SafeSendAsync(order, NotificationKind.OrderCancelled, BuildBody(NotificationKind.OrderCancelled, order.Id), null);

        return new OrderActionResult(ActionOutcome.Ok, order.Id, order.Status.ToString(), null);
    }

    // ---- Flow 2: reading orders / notifications ----

    public async Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOwnerSpecification(buyerId));
        await RefreshStatusesAsync(notifications);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        return orders.Select(order =>
        {
            var items = order.OrderItems
                .Select(i => new OrderLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.Units, i.UnitPrice))
                .ToList();
            var views = byOrder.TryGetValue(order.Id, out var list)
                ? list.Select(ToView).ToList()
                : new List<NotificationView>();
            return new OrderView(order.Id, order.Status.ToString(), order.OrderDate, order.Total(), items, views);
        }).ToList();
    }

    public async Task<OrderNotificationsResult> GetOrderNotificationsAsync(int orderId, string callerId, bool isAdmin)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            return new OrderNotificationsResult(ActionOutcome.NotFound, Array.Empty<NotificationView>(), "Order not found.");
        }
        if (!isAdmin && !string.Equals(order.BuyerId, callerId, StringComparison.Ordinal))
        {
            return new OrderNotificationsResult(ActionOutcome.Forbidden, Array.Empty<NotificationView>(),
                "This order belongs to another shopper.");
        }

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId));
        await RefreshStatusesAsync(notifications);
        return new OrderNotificationsResult(ActionOutcome.Ok, notifications.Select(ToView).ToList(), null);
    }

    // ---- Flow 3: operator actions ----

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return new ResendResult(ActionOutcome.BadRequest, 0, string.Empty, "An idempotency key is required.");
        }

        // A repeat under the same key must not send a second message.
        var priorForKey = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey));
        if (priorForKey is not null)
        {
            _logger.LogInformation("Resend request for key was already handled; returning existing notification id={Id}.",
                priorForKey.Id);
            return new ResendResult(ActionOutcome.Ok, priorForKey.Id, priorForKey.Status, null);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId);
        if (original is null)
        {
            return new ResendResult(ActionOutcome.NotFound, 0, string.Empty, "Notification not found.");
        }

        // Reflect the provider's latest view before deciding whether a resend is warranted.
        await RefreshStatusesAsync(new[] { original });
        if (!original.IsDeliveryFailure())
        {
            return new ResendResult(ActionOutcome.Conflict, original.Id, original.Status,
                "Only a message that did not reach the shopper can be re-sent.");
        }

        var body = original.Body ?? BuildBody(original.Kind, original.OrderId);
        var resend = new OrderNotification(original.OrderId, original.OwnerId, original.ToPhoneNumber, original.Kind, body);
        resend.SetIdempotencyKey(idempotencyKey);
        resend.SetResendOf(original.Id);

        try
        {
            var provider = await _twilio.SendMessageAsync(original.ToPhoneNumber, body);
            resend.SetProviderResult(provider.Sid, provider.Status, provider.ErrorCode);
        }
        catch (Exception ex)
        {
            resend.SetSendFailed((ex as TwilioApiException)?.ProviderErrorCode);
            _logger.LogWarning("Resend send attempt failed for original notification id={Id}.", original.Id);
        }

        // Persist regardless of send outcome so the idempotency key is recorded and repeats dedupe.
        await _notificationRepository.AddAsync(resend);
        return new ResendResult(ActionOutcome.Ok, resend.Id, resend.Status, null);
    }

    public async Task<DisposeContentResult> DisposeContentAsync(int notificationId)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId);
        if (notification is null)
        {
            return new DisposeContentResult(ActionOutcome.NotFound, "Notification not found.");
        }
        if (notification.ContentRedacted)
        {
            return new DisposeContentResult(ActionOutcome.Ok, null); // already disposed
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                await _twilio.RedactMessageBodyAsync(notification.ProviderMessageSid!);
            }
            catch (TwilioApiException)
            {
                // We must not claim success if the provider still holds the text.
                return new DisposeContentResult(ActionOutcome.Conflict,
                    "The message content could not be disposed of at the provider; please retry.");
            }
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification);
        _logger.LogInformation("Disposed content of notification id={Id}.", notification.Id);
        return new DisposeContentResult(ActionOutcome.Ok, null);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var fromNumber = _settings.FromNumber;

        // Ask the provider only for this application's own number's messages in the range.
        var providerMessages = await _twilio.ListMessagesAsync(fromNumber, from, to);

        // eShop's "believes it sent" set: notifications carrying a provider SID in the range that
        // were actually dispatched (exclude still-scheduled and cancelled).
        var recorded = await _notificationRepository.ListAsync(new NotificationsWithSidInRangeSpecification(from, to));
        var eShopSent = recorded
            .Where(n => !string.Equals(n.Status, MessageStatuses.Scheduled, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(n.Status, MessageStatuses.Canceled, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());
        var eShopBySid = eShopSent
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var onlyInProvider = new List<ReconciliationProviderOnly>();
        var onlyInEShop = new List<ReconciliationEShopOnly>();

        foreach (var (sid, msg) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var n))
            {
                matched.Add(new ReconciliationMatch(sid, n.Id, n.OrderId, n.Kind.ToString(), msg.Status, n.Status));
            }
            else
            {
                onlyInProvider.Add(new ReconciliationProviderOnly(sid, msg.Status, msg.To, msg.DateSent));
            }
        }

        foreach (var n in eShopSent)
        {
            if (n.ProviderMessageSid is null || !providerBySid.ContainsKey(n.ProviderMessageSid))
            {
                onlyInEShop.Add(new ReconciliationEShopOnly(n.Id, n.ProviderMessageSid, n.OrderId, n.Kind.ToString(), n.Status));
            }
        }

        _logger.LogInformation("Reconciliation: provider={Provider} eShop={EShop} matched={Matched}.",
            providerMessages.Count, eShopSent.Count, matched.Count);

        return new ReconciliationReport(from, to, fromNumber, providerMessages.Count, eShopSent.Count, matched.Count,
            matched, onlyInProvider, onlyInEShop);
    }

    // ---- helpers ----

    /// <summary>
    /// Sends (or schedules) a message to every number the order's owner has on file, recording a
    /// notification per number. Never throws: a message that cannot be sent must not fail the
    /// underlying order operation, and a shopper with no number on file is simply not messaged.
    /// </summary>
    private async Task SafeSendAsync(Order order, NotificationKind kind, string body, DateTimeOffset? scheduleAt)
    {
        try
        {
            var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId));
            if (numbers.Count == 0)
            {
                _logger.LogInformation("Order id={OrderId} owner has no number on file; not messaged for {Kind}.",
                    order.Id, kind);
                return;
            }

            foreach (var number in numbers)
            {
                var notification = new OrderNotification(order.Id, order.BuyerId, number.PhoneNumber, kind, body, scheduleAt);
                try
                {
                    var provider = scheduleAt.HasValue
                        ? await _twilio.ScheduleMessageAsync(number.PhoneNumber, body, scheduleAt.Value)
                        : await _twilio.SendMessageAsync(number.PhoneNumber, body);
                    notification.SetProviderResult(provider.Sid, provider.Status, provider.ErrorCode);
                }
                catch (Exception ex)
                {
                    notification.SetSendFailed((ex as TwilioApiException)?.ProviderErrorCode);
                    _logger.LogWarning("Could not send {Kind} notification for order id={OrderId}; recorded as failed.",
                        kind, order.Id);
                }

                await _notificationRepository.AddAsync(notification);
            }
        }
        catch (Exception)
        {
            // Best-effort: never let notification work surface out of an order operation.
            _logger.LogWarning("Notification dispatch for order id={OrderId} kind={Kind} did not complete.", order.Id, kind);
        }
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId)
    {
        try
        {
            var scheduled = await _notificationRepository.ListAsync(new ScheduledFollowUpsForOrderSpecification(orderId));
            foreach (var followUp in scheduled)
            {
                try
                {
                    await _twilio.CancelScheduledMessageAsync(followUp.ProviderMessageSid!);
                    followUp.MarkCanceled();
                }
                catch (Exception)
                {
                    // Reflect the provider's cancellation if it reports the message already moved on.
                    followUp.MarkCanceled();
                    _logger.LogWarning("Provider cancel of scheduled follow-up id={Id} did not confirm cleanly.", followUp.Id);
                }
                await _notificationRepository.UpdateAsync(followUp);
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Cancelling scheduled follow-ups for order id={OrderId} did not complete.", orderId);
        }
    }

    /// <summary>Refreshes each non-terminal, provider-backed notification from the provider.</summary>
    private async Task RefreshStatusesAsync(IReadOnlyList<OrderNotification> notifications)
    {
        foreach (var n in notifications)
        {
            if (string.IsNullOrEmpty(n.ProviderMessageSid) || MessageStatuses.IsTerminal(n.Status))
            {
                continue;
            }
            try
            {
                var provider = await _twilio.GetMessageAsync(n.ProviderMessageSid!);
                if (!string.Equals(provider.Status, n.Status, StringComparison.OrdinalIgnoreCase)
                    || (provider.ErrorCode.HasValue && provider.ErrorCode != n.ErrorCode))
                {
                    n.UpdateStatus(provider.Status, provider.ErrorCode);
                    await _notificationRepository.UpdateAsync(n);
                }
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh status for notification id={Id}.", n.Id);
            }
        }
    }

    private static NotificationView ToView(OrderNotification n) => new(
        n.Id, n.OrderId, n.Kind.ToString(), n.Status, n.ErrorCode, n.ContentRedacted,
        n.ScheduledSendAt.HasValue, n.ScheduledSendAt, n.ResendOfNotificationId, n.ProviderMessageSid, n.CreatedDate);

    private static string BuildBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShop: thanks! Your order #{orderId} has been placed. We'll text you as it moves.",
        NotificationKind.OrderDispatched => $"eShop: good news - your order #{orderId} is on its way!",
        NotificationKind.DeliveryFollowUp => $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        NotificationKind.OrderCancelled => $"eShop: your order #{orderId} has been cancelled. Contact support if this was unexpected.",
        _ => $"eShop: an update about your order #{orderId}."
    };
}
