using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far ahead the "how did the delivery go?" follow-up is queued with the provider.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    /// <summary>
    /// POST /api/orders carries only catalog items and quantities, so orders get a placeholder ship-to
    /// address. Notifications, not fulfilment, are what this feature is about.
    /// </summary>
    private static Address DefaultShipToAddress() => new("N/A", "N/A", "N/A", "N/A", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IContactNumberService _contactNumbers;
    private readonly ISmsMessagingService _sms;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<OrderNotification> notificationRepository,
        IContactNumberService contactNumbers,
        ISmsMessagingService sms,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _notificationRepository = notificationRepository;
        _contactNumbers = contactNumbers;
        _sms = sms;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineItem> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(lines));
            Guard.Against.NegativeOrZero(line.Quantity, nameof(line.Quantity));

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipToAddress(), orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        // Tell the shopper their order was placed. Never let a messaging failure fail the order.
        await SendOrderMessageAsync(order, NotificationKind.OrderPlaced, cancellationToken);

        return order;
    }

    public async Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return false;
        }

        // Tell the shopper it is on its way.
        await SendOrderMessageAsync(order, NotificationKind.OrderDispatched, cancellationToken);

        // Queue the delayed "how did the delivery go?" follow-up with the provider (not held here).
        await ScheduleFollowUpAsync(order, cancellationToken);

        return true;
    }

    public async Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return false;
        }

        // Call off any follow-up that has not yet gone out, so it never reaches the customer.
        await CancelPendingFollowUpsAsync(orderId, cancellationToken);

        // Tell the shopper the order was cancelled.
        await SendOrderMessageAsync(order, NotificationKind.OrderCancelled, cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetNotificationsForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);

        if (refreshFromProvider)
        {
            foreach (var notification in notifications.Where(n => n.HasProviderMessage && !n.IsTerminal))
            {
                try
                {
                    var current = await _sms.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                    notification.UpdateDeliveryState(current.Status, current.ErrorCode, current.ErrorMessage);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
                catch (Exception)
                {
                    _logger.LogWarning("Could not refresh delivery status for notification {0}; returning last known state.", notification.Id);
                }
            }
        }

        return notifications;
    }

    public async Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Repeating a request under the same key must not send a second message.
        var alreadyDone = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyDone is not null)
        {
            return new ResendResult { Notification = alreadyDone, ReusedExisting = true };
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return null;
        }

        // Regenerate the standard text if the original content has since been disposed of.
        var body = original.Body ?? BuildBody(original.Kind, original.OrderId);
        var resend = original.CreateResend(idempotencyKey, body);

        try
        {
            var result = await _sms.SendAsync(original.ToNumber, body, cancellationToken);
            resend.RecordAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, null);
        }
        catch (Exception)
        {
            resend.RecordSendFailure();
            _logger.LogWarning("Re-send of notification {0} could not be accepted by the provider.", notificationId);
        }

        await _notificationRepository.AddAsync(resend, cancellationToken);
        return new ResendResult { Notification = resend, ReusedExisting = false };
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Redact the text at the provider so it is no longer retrievable there. If the provider rejects
        // the redaction, surface that rather than falsely claiming the content was disposed of.
        if (notification.HasProviderMessage)
        {
            await _sms.RedactAsync(notification.ProviderMessageSid!, cancellationToken);
        }

        notification.DisposeContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {0}.", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _sms.ListSentFromConfiguredSenderAsync(from, to, cancellationToken);

        var localWithSid = await _notificationRepository.ListAsync(new OrderNotificationsWithProviderMessageSpecification(), cancellationToken);
        var localBySid = localWithSid
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var providerSids = new HashSet<string>(providerMessages.Where(m => !string.IsNullOrEmpty(m.Sid)).Select(m => m.Sid));

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();

        foreach (var message in providerMessages)
        {
            if (!string.IsNullOrEmpty(message.Sid) && localBySid.TryGetValue(message.Sid, out var local))
            {
                matched.Add(new ReconciliationEntry
                {
                    Sid = message.Sid,
                    ProviderStatus = message.Status,
                    EShopStatus = local.DeliveryStatus,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    Kind = local.Kind.ToString(),
                    DateSent = message.DateSent,
                    ErrorCode = message.ErrorCode
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    Sid = message.Sid,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent,
                    ErrorCode = message.ErrorCode
                });
            }
        }

        // eShop believes it sent these (created in range, has a SID) but the provider's list omits them.
        var eShopOnly = localWithSid
            .Where(n => n.CreatedAt >= from && n.CreatedAt <= to && !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEntry
            {
                Sid = n.ProviderMessageSid,
                EShopStatus = n.DeliveryStatus,
                NotificationId = n.Id,
                OrderId = n.OrderId,
                Kind = n.Kind.ToString()
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = string.Empty, // filled in by the endpoint/provider layer that knows the configured number
            ProviderCount = providerMessages.Count,
            EShopCount = localWithSid.Count,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly
        };
    }

    // ----- helpers -------------------------------------------------------------------------------

    private async Task<OrderNotification?> SendOrderMessageAsync(Order order, NotificationKind kind, CancellationToken cancellationToken)
    {
        var contact = await _contactNumbers.GetReachableNumberAsync(order.BuyerId, cancellationToken);
        if (contact is null)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation("No contact number on file for order {0}; skipping {1} message.", order.Id, kind);
            return null;
        }

        var body = BuildBody(kind, order.Id);
        var notification = new OrderNotification(order.Id, order.BuyerId, kind, contact.PhoneNumber, body);

        try
        {
            var result = await _sms.SendAsync(contact.PhoneNumber, body, cancellationToken);
            notification.RecordAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, null);
        }
        catch (Exception)
        {
            // Never let a messaging failure fail the underlying order operation.
            notification.RecordSendFailure();
            _logger.LogWarning("Provider did not accept the {0} message for order {1}.", kind, order.Id);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
        return notification;
    }

    private async Task ScheduleFollowUpAsync(Order order, CancellationToken cancellationToken)
    {
        var contact = await _contactNumbers.GetReachableNumberAsync(order.BuyerId, cancellationToken);
        if (contact is null)
        {
            return;
        }

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = BuildBody(NotificationKind.DeliveryFollowUp, order.Id);
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, contact.PhoneNumber, body);

        try
        {
            var result = await _sms.ScheduleAsync(contact.PhoneNumber, body, sendAt, cancellationToken);
            notification.RecordAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, result.ScheduledFor ?? sendAt);
        }
        catch (Exception)
        {
            notification.RecordSendFailure();
            _logger.LogWarning("Provider did not accept the scheduled follow-up for order {0}.", order.Id);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        var pendingFollowUps = notifications.Where(n =>
            n.Kind == NotificationKind.DeliveryFollowUp &&
            n.HasProviderMessage &&
            !n.IsTerminal);

        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                var result = await _sms.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateDeliveryState(result.Status, result.ErrorCode, result.ErrorMessage);
                if (!followUp.IsTerminal)
                {
                    // Reflect the cancellation even if the provider's echoed status lagged.
                    followUp.MarkCancelled();
                }
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Cancelled scheduled follow-up notification {0} for order {1}.", followUp.Id, orderId);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up notification {0} for order {1}.", followUp.Id, orderId);
            }
        }
    }

    private static string BuildBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced =>
            $"eShopOnWeb: your order #{orderId} has been placed. Thank you for shopping with us!",
        NotificationKind.OrderDispatched =>
            $"eShopOnWeb: good news - your order #{orderId} has been dispatched and is on its way!",
        NotificationKind.DeliveryFollowUp =>
            $"eShopOnWeb: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        NotificationKind.OrderCancelled =>
            $"eShopOnWeb: your order #{orderId} has been cancelled. If this is unexpected, please contact us.",
        _ => $"eShopOnWeb: an update on your order #{orderId}."
    };
}
