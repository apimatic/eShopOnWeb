using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // Statuses that are settled — no point asking the provider again on read.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        SmsDeliveryStatus.Delivered,
        SmsDeliveryStatus.Undelivered,
        SmsDeliveryStatus.Failed,
        SmsDeliveryStatus.Canceled,
        SmsDeliveryStatus.SendFailed
    };

    // Statuses that mean eShop believes the message actually went to the carrier (vs. scheduled,
    // pending, cancelled or never sent). Used to scope the eShop side of reconciliation.
    private static readonly HashSet<string> BelievedSentStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        SmsDeliveryStatus.Queued,
        SmsDeliveryStatus.Accepted,
        SmsDeliveryStatus.Sending,
        SmsDeliveryStatus.Sent,
        SmsDeliveryStatus.Delivered,
        SmsDeliveryStatus.Read,
        SmsDeliveryStatus.Undelivered,
        SmsDeliveryStatus.Failed
    };

    // Serialises resend by idempotency key so concurrent duplicates cannot both send.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly ISmsSender _smsSender;
    private readonly IUriComposer _uriComposer;
    private readonly NotificationSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<Notification> notificationRepository,
        ISmsSender smsSender,
        IUriComposer uriComposer,
        NotificationSettings settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsSender = smsSender;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    public async Task<OrderPlacementResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (lines is null || lines.Count == 0)
        {
            return OrderPlacementResult.Invalid("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            return OrderPlacementResult.Invalid("Every item quantity must be greater than zero.");
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds));
        if (catalogItems.Count != catalogItemIds.Length)
        {
            return OrderPlacementResult.Invalid("One or more catalog items do not exist.");
        }

        // Reuse the app's existing order/order-item model rather than a parallel one.
        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, PlaceholderAddress(), items);
        await _orderRepository.AddAsync(order);
        _logger.LogInformation("Placed order {OrderId} for buyer {BuyerId}.", order.Id, buyerId);

        await NotifyBuyerNumbersAsync(order, NotificationKind.OrderPlaced, PlacedBody(order));
        return OrderPlacementResult.Success(order);
    }

    public async Task<OrderOperationResult> DispatchOrderAsync(int orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            return OrderOperationResult.NotFound();
        }

        try
        {
            order.Dispatch();
        }
        catch (InvalidOrderStateException ex)
        {
            return OrderOperationResult.InvalidState(ex.Message);
        }

        await _orderRepository.UpdateAsync(order);
        _logger.LogInformation("Dispatched order {OrderId}.", order.Id);

        await NotifyBuyerNumbersAsync(order, NotificationKind.OrderDispatched, DispatchedBody(order));
        await ScheduleFollowUpAsync(order);
        return OrderOperationResult.Success(order);
    }

    public async Task<OrderOperationResult> CancelOrderAsync(int orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            return OrderOperationResult.NotFound();
        }

        try
        {
            order.Cancel();
        }
        catch (InvalidOrderStateException ex)
        {
            return OrderOperationResult.InvalidState(ex.Message);
        }

        await _orderRepository.UpdateAsync(order);
        _logger.LogInformation("Cancelled order {OrderId}.", order.Id);

        // Call off any not-yet-sent delivery follow-up first: a "how did delivery go?" message for a
        // cancelled order is exactly the incident this must prevent.
        await CancelScheduledFollowUpsAsync(order.Id);

        await NotifyBuyerNumbersAsync(order, NotificationKind.OrderCancelled, CancelledBody(order));
        return OrderOperationResult.Success(order);
    }

    public async Task<OrderNotificationsView?> GetOrderNotificationsAsync(int orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId));
        await RefreshStatusesAsync(notifications);
        return new OrderNotificationsView
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Notifications = notifications
        };
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var result = new List<OrderWithNotifications>(orders.Count);
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id));
            await RefreshStatusesAsync(notifications);
            result.Add(new OrderWithNotifications { Order = order, Notifications = notifications });
        }
        return result;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var gate = ResendLocks.GetOrAdd(idempotencyKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            // Idempotency: a resend already produced under this key is returned without sending again.
            var existing = await _notificationRepository.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey));
            if (existing is not null)
            {
                _logger.LogInformation("Resend under idempotency key reused existing notification {NotificationId}.", existing.Id);
                return ResendResult.AlreadyHandled(existing);
            }

            var original = await _notificationRepository.GetByIdAsync(notificationId);
            if (original is null)
            {
                return ResendResult.NotFound();
            }
            if (string.IsNullOrEmpty(original.Body))
            {
                return ResendResult.Failed("The original message content is no longer available to resend.");
            }

            var resend = new Notification(original.BuyerId, original.OrderId, NotificationKind.Resend, original.ToNumber, original.Body);
            resend.SetIdempotencyKey(idempotencyKey);
            resend.SetResendOf(original.Id);
            // Persist before sending so a concurrent duplicate (different process) still sees the key.
            await _notificationRepository.AddAsync(resend);

            try
            {
                var result = await _smsSender.SendAsync(resend.ToNumber, original.Body);
                resend.RecordProviderResult(result.Sid, result.Status, result.DateSent, result.ErrorCode, result.ErrorMessage);
            }
            catch (Exception ex)
            {
                resend.RecordSendFailure(ex.Message);
                _logger.LogWarning("Resend of notification {NotificationId} failed to reach the provider.", original.Id);
            }

            await _notificationRepository.UpdateAsync(resend);
            return ResendResult.Sent(resend);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ContentDisposalResult> DisposeContentAsync(int notificationId)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId);
        if (notification is null)
        {
            return ContentDisposalResult.NotFound();
        }

        // Redact at the provider first so the text is no longer retrievable there. Only then clear the
        // local copy — if the provider redaction fails the exception propagates and nothing is lost
        // silently. The record of the message (and what became of it) survives either way.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _smsSender.RedactBodyAsync(notification.ProviderMessageSid);
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification);
        _logger.LogInformation("Disposed content of notification {NotificationId}.", notificationId);
        return ContentDisposalResult.Disposed(notification);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var fromNumber = _smsSender.FromNumber;

        // Provider side: ask for this number's messages directly.
        var providerRecords = await _smsSender.ListSentFromAsync(fromNumber, from, to);
        var providerBySid = providerRecords
            .Where(r => !string.IsNullOrEmpty(r.Sid))
            .GroupBy(r => r.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        // eShop side: everything it believes it handed to the provider, narrowed to the range and to
        // messages it believes actually went to the carrier.
        var candidates = await _notificationRepository.ListAsync(new NotificationsWithProviderSidSpecification());
        var eShopBelieved = candidates
            .Where(n => n.ProviderMessageSid is not null
                && BelievedSentStatuses.Contains(n.ProviderStatus))
            .Where(n =>
            {
                var effective = n.ProviderDateSent ?? n.CreatedDate;
                return effective >= from && effective <= to;
            })
            .ToList();
        var eShopBySid = eShopBelieved
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = fromNumber,
            ProviderCount = providerBySid.Count,
            EShopCount = eShopBySid.Count
        };

        foreach (var (sid, record) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var notification))
            {
                report.Matched.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = sid,
                    NotificationId = notification.Id,
                    ProviderStatus = record.Status,
                    EShopStatus = notification.ProviderStatus,
                    DateSent = record.DateSent
                });
            }
            else
            {
                report.ProviderOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = sid,
                    ProviderStatus = record.Status,
                    DateSent = record.DateSent
                });
            }
        }

        foreach (var (sid, notification) in eShopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                report.EShopOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = sid,
                    NotificationId = notification.Id,
                    EShopStatus = notification.ProviderStatus,
                    DateSent = notification.ProviderDateSent ?? notification.CreatedDate
                });
            }
        }

        _logger.LogInformation("Reconciliation over range produced {Matched} matched, {ProviderOnly} provider-only, {EShopOnly} eShop-only.",
            report.Matched.Count, report.ProviderOnly.Count, report.EShopOnly.Count);
        return report;
    }

    // ----- internal helpers ----------------------------------------------------------------------

    private async Task NotifyBuyerNumbersAsync(Order order, NotificationKind kind, string body)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId));
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            return;
        }

        foreach (var number in numbers)
        {
            var notification = new Notification(order.BuyerId, order.Id, kind, number.PhoneNumber, body);
            await _notificationRepository.AddAsync(notification);

            try
            {
                var result = await _smsSender.SendAsync(number.PhoneNumber, body);
                notification.RecordProviderResult(result.Sid, result.Status, result.DateSent, result.ErrorCode, result.ErrorMessage);
            }
            catch (Exception ex)
            {
                // A message that cannot be sent never fails the underlying operation.
                notification.RecordSendFailure(ex.Message);
                _logger.LogWarning("Notification of kind {Kind} for order {OrderId} failed to reach the provider.", kind, order.Id);
            }

            await _notificationRepository.UpdateAsync(notification);
        }
    }

    private async Task ScheduleFollowUpAsync(Order order)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId));
        if (numbers.Count == 0)
        {
            return;
        }

        var sendAt = DateTimeOffset.UtcNow.Add(_settings.DeliveryFollowUpDelay);
        var body = FollowUpBody(order);

        foreach (var number in numbers)
        {
            var notification = new Notification(order.BuyerId, order.Id, NotificationKind.DeliveryFollowUp, number.PhoneNumber, body);
            await _notificationRepository.AddAsync(notification);

            try
            {
                // Queued with the provider for later — this application keeps no timer of its own.
                var result = await _smsSender.ScheduleAsync(number.PhoneNumber, body, sendAt);
                notification.RecordProviderResult(result.Sid, result.Status, result.DateSent, result.ErrorCode, result.ErrorMessage);
                notification.MarkScheduled(sendAt);
            }
            catch (Exception ex)
            {
                notification.RecordSendFailure(ex.Message);
                _logger.LogWarning("Delivery follow-up for order {OrderId} could not be scheduled with the provider.", order.Id);
            }

            await _notificationRepository.UpdateAsync(notification);
        }
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId)
    {
        var followUps = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId));
        foreach (var followUp in followUps)
        {
            try
            {
                await _smsSender.CancelScheduledAsync(followUp.ProviderMessageSid!);
                followUp.MarkCancelled();
                await _notificationRepository.UpdateAsync(followUp);
            }
            catch (Exception)
            {
                // Surface as a warning but do not fail the cancellation of the order itself.
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
        }
    }

    private async Task RefreshStatusesAsync(IReadOnlyList<Notification> notifications)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }
            if (TerminalStatuses.Contains(notification.ProviderStatus))
            {
                continue;
            }

            try
            {
                var latest = await _smsSender.FetchStatusAsync(notification.ProviderMessageSid);
                notification.UpdateDeliveryStatus(latest.Status, latest.DateSent, latest.ErrorCode, latest.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification);
            }
            catch (Exception)
            {
                // A read failure must not break reporting; keep the last-known status.
                _logger.LogWarning("Could not refresh delivery status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private static Address PlaceholderAddress() =>
        // Shipping is out of scope for this notification flow; the order model requires a non-null
        // address, so a placeholder is used. A real checkout supplies the shopper's address.
        new("N/A", "N/A", "N/A", "N/A", "N/A");

    private static string PlacedBody(Order order) =>
        $"eShop: Thanks! Your order #{order.Id} has been placed. We'll text you as it progresses.";

    private static string DispatchedBody(Order order) =>
        $"eShop: Good news - your order #{order.Id} is on its way!";

    private static string FollowUpBody(Order order) =>
        $"eShop: How did the delivery of your order #{order.Id} go? We'd love your feedback.";

    private static string CancelledBody(Order order) =>
        $"eShop: Your order #{order.Id} has been cancelled. If this is unexpected, please contact us.";
}
