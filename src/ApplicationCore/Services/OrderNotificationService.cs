using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates order lifecycle transitions and the SMS notifications that go out as an order moves.
/// Business rules honored here:
/// <list type="bullet">
/// <item>A message that cannot be sent never fails the underlying operation.</item>
/// <item>A shopper with no number on file is simply not messaged.</item>
/// <item>The dispatch follow-up is queued with the provider (scheduled), not held by a timer here.</item>
/// <item>Cancelling an order calls off any follow-up that has not yet gone out.</item>
/// <item>The destination number is persisted but never logged.</item>
/// </list>
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // "a few days later" — well within Twilio's 15-minutes..7-days scheduling window.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled"
    };

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsSender _smsSender;
    private readonly IUriComposer _uriComposer;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsSender smsSender,
        IUriComposer uriComposer,
        TwilioSettings settings,
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

    public async Task<Result<Order>> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
            return Result<Order>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "items", ErrorMessage = "At least one order item is required." }
            });

        if (lines.Any(l => l.Quantity <= 0))
            return Result<Order>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "items", ErrorMessage = "Every item quantity must be greater than zero." }
            });

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            return Result<Order>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "items", ErrorMessage = $"Unknown catalog item id(s): {string.Join(", ", missing)}." }
            });

        // Reuse the existing order/order-item model rather than a parallel one.
        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipToAddress ?? new Address("N/A", "N/A", "N/A", "N/A", "N/A");
        var order = new Order(buyerId, address, items);
        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation($"Placed order (id={order.Id}) for buyer.");

        // Tell the shopper their order was placed. Messaging is best-effort; the order is already saved.
        await NotifyAsync(order, NotificationKind.OrderPlaced, PlacedBody(order.Id), sendAt: null, cancellationToken);

        return Result<Order>.Success(order);
    }

    public async Task<Result> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return Result.NotFound();

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Error(ex.Message);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Order dispatched (id={order.Id}).");

        // Tell the shopper it is on its way (immediate)...
        await NotifyAsync(order, NotificationKind.OrderDispatched, DispatchedBody(order.Id), sendAt: null, cancellationToken);
        // ...and queue the "how did delivery go" follow-up WITH the provider for a few days later.
        await NotifyAsync(order, NotificationKind.DeliveryFollowUp, FollowUpBody(order.Id),
            sendAt: DateTimeOffset.UtcNow.Add(FollowUpDelay), cancellationToken);

        return Result.Success();
    }

    public async Task<Result> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return Result.NotFound();

        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Error(ex.Message);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Order cancelled (id={order.Id}).");

        // Call off any follow-up that has not yet gone out, so a "how did delivery go" message never
        // reaches a customer whose order was cancelled.
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var pending in notifications.Where(n => n.IsPendingSchedule))
        {
            try
            {
                var state = await _smsSender.CancelScheduledAsync(pending.ProviderMessageSid!, cancellationToken);
                pending.RefreshDeliveryState(state);
                await _notificationRepository.UpdateAsync(pending, cancellationToken);
                _logger.LogInformation($"Called off scheduled follow-up (notificationId={pending.Id}).");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to call off scheduled follow-up (notificationId={pending.Id}): {Describe(ex)}.");
            }
        }

        // Tell the shopper the order was cancelled.
        await NotifyAsync(order, NotificationKind.OrderCancelled, CancelledBody(order.Id), sendAt: null, cancellationToken);

        return Result.Success();
    }

    public async Task<Result<OrderNotification>> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Result<OrderNotification>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "idempotencyKey", ErrorMessage = "An idempotency key is required." }
            });

        // Repeat under the same key: return the notification the first attempt produced, no second send.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation($"Resend idempotency hit for key; returning notification (id={existing.Id}).");
            return Result<OrderNotification>.Success(existing);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
            return Result<OrderNotification>.NotFound();

        // The resent message reproduces the original event's text to the same destination, sent now.
        var body = BodyFor(original.Kind, original.OrderId);
        var resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationKind.Resend, original.ToNumber);
        resend.AssignIdempotencyKey(idempotencyKey);

        try
        {
            var state = await _smsSender.SendAsync(new SmsSendRequest(original.ToNumber, body), cancellationToken);
            resend.RecordProviderResult(state);
            _logger.LogInformation($"Resent notification (originalId={notificationId}, newId pending persist).");
        }
        catch (Exception ex)
        {
            resend.RecordSendFailure(Describe(ex));
            _logger.LogWarning($"Resend failed to send (originalId={notificationId}): {Describe(ex)}.");
        }

        // Persist even on failure so a repeat under the same key does not send again.
        await _notificationRepository.AddAsync(resend, cancellationToken);
        return Result<OrderNotification>.Success(resend);
    }

    public async Task<Result> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            return Result.NotFound();

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                // Redaction must reach the provider — not merely be hidden by this application.
                await _smsSender.RedactBodyAsync(notification.ProviderMessageSid!, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to redact message content at provider (notificationId={notificationId}): {Describe(ex)}.");
                return Result.Error("The message content could not be disposed of at the provider.");
            }
        }

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation($"Disposed of message content (notificationId={notificationId}).");
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<OrderNotification>>> GetOrderNotificationsAsync(int orderId,
        string requestingBuyerId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return Result<IReadOnlyList<OrderNotification>>.NotFound();

        // Shopper-scoped: only the owner of the order may see its notifications.
        if (!string.Equals(order.BuyerId, requestingBuyerId, StringComparison.Ordinal))
            return Result<IReadOnlyList<OrderNotification>>.Forbidden();

        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId), cancellationToken);

        await RefreshDeliveryStatesAsync(notifications, cancellationToken);
        return Result<IReadOnlyList<OrderNotification>>.Success(notifications);
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
            return Array.Empty<OrderWithNotifications>();

        var orderIds = orders.Select(o => o.Id).ToArray();
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderIdsSpecification(orderIds), cancellationToken);

        await RefreshDeliveryStatesAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());
        return orders
            .Select(o => new OrderWithNotifications(o,
                byOrder.TryGetValue(o.Id, out var list)
                    ? list
                    : (IReadOnlyList<OrderNotification>)Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.FromNumber))
            throw new InvalidOperationException("Twilio:FromNumber is required to reconcile.");

        // Ask the provider directly for THIS sending number's messages in the range — the account
        // carries traffic that is not this application's, so we scope by From rather than filter later.
        var providerMessages = await _smsSender.ListMessagesAsync(_settings.FromNumber!, from, to, cancellationToken);

        // What eShop believes it sent in the range (notifications that carry a provider message id).
        var withSid = await _notificationRepository.ListAsync(
            new OrderNotificationsWithProviderIdSpecification(), cancellationToken);
        var localInRange = withSid
            .Where(n => n.CreatedAt >= from && n.CreatedAt <= to)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var providerBySid = providerMessages
            .GroupBy(m => m.Sid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _settings.FromNumber,
            ProviderMessageCount = providerBySid.Count,
            EShopMessageCount = localInRange.Count
        };

        foreach (var (sid, providerState) in providerBySid)
        {
            if (localInRange.TryGetValue(sid, out var local))
            {
                report.Matched.Add(new ReconciliationEntry
                {
                    Sid = sid,
                    ProviderStatus = providerState.Status,
                    EShopStatus = local.ProviderStatus,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    DateSent = providerState.DateSent,
                    Presence = "both"
                });
            }
            else
            {
                report.ProviderOnly.Add(new ReconciliationEntry
                {
                    Sid = sid,
                    ProviderStatus = providerState.Status,
                    DateSent = providerState.DateSent,
                    Presence = "providerOnly"
                });
            }
        }

        foreach (var (sid, local) in localInRange)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                report.EShopOnly.Add(new ReconciliationEntry
                {
                    Sid = sid,
                    EShopStatus = local.ProviderStatus,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    Presence = "eShopOnly"
                });
            }
        }

        report.MatchedCount = report.Matched.Count;
        report.ProviderOnlyCount = report.ProviderOnly.Count;
        report.EShopOnlyCount = report.EShopOnly.Count;
        _logger.LogInformation(
            $"Reconciliation over range: provider={report.ProviderMessageCount}, eShop={report.EShopMessageCount}, " +
            $"matched={report.MatchedCount}, providerOnly={report.ProviderOnlyCount}, eShopOnly={report.EShopOnlyCount}.");
        return report;
    }

    /// <summary>
    /// Sends (or schedules) a message to every registered number of the order's owner, recording each
    /// attempt. A send that throws is recorded as a failure and never propagates. No number on file =
    /// no message.
    /// </summary>
    private async Task NotifyAsync(Order order, NotificationKind kind, string body, DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            _logger.LogInformation($"No contact number on file for order (id={order.Id}, kind={kind}); not messaging.");
            return;
        }

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, kind, number.PhoneNumber);
            try
            {
                var state = await _smsSender.SendAsync(new SmsSendRequest(number.PhoneNumber, body, sendAt), cancellationToken);
                notification.RecordProviderResult(state);
                _logger.LogInformation($"Notification sent (orderId={order.Id}, kind={kind}, sid={state.Sid}, status={state.Status}).");
            }
            catch (Exception ex)
            {
                // A message that cannot be sent must never fail the underlying operation.
                notification.RecordSendFailure(Describe(ex));
                _logger.LogWarning($"Notification failed to send (orderId={order.Id}, kind={kind}): {Describe(ex)}.");
            }

            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
    }

    private async Task RefreshDeliveryStatesAsync(IEnumerable<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var n in notifications)
        {
            if (string.IsNullOrEmpty(n.ProviderMessageSid))
                continue;
            if (n.ProviderStatus != null && TerminalStatuses.Contains(n.ProviderStatus))
                continue;

            try
            {
                var state = await _smsSender.FetchAsync(n.ProviderMessageSid!, ct);
                if (state is not null)
                {
                    n.RefreshDeliveryState(state);
                    await _notificationRepository.UpdateAsync(n, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not refresh delivery state (notificationId={n.Id}): {Describe(ex)}.");
            }
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        Exceptions.SmsProviderException t => $"provider error (HTTP {t.HttpStatusCode.ToString(CultureInfo.InvariantCulture)}, code {t.ProviderErrorCode?.ToString(CultureInfo.InvariantCulture) ?? "n/a"})",
        InvalidOperationException => "configuration/state error",
        _ => ex.GetType().Name
    };

    private static string BodyFor(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => PlacedBody(orderId),
        NotificationKind.OrderDispatched => DispatchedBody(orderId),
        NotificationKind.DeliveryFollowUp => FollowUpBody(orderId),
        NotificationKind.OrderCancelled => CancelledBody(orderId),
        _ => DispatchedBody(orderId)
    };

    private static string PlacedBody(int orderId) =>
        $"eShopOnWeb: your order #{orderId} has been placed. Thank you for shopping with us!";

    private static string DispatchedBody(int orderId) =>
        $"eShopOnWeb: good news — your order #{orderId} is on its way!";

    private static string FollowUpBody(int orderId) =>
        $"eShopOnWeb: how did the delivery of your order #{orderId} go? We'd love your feedback.";

    private static string CancelledBody(int orderId) =>
        $"eShopOnWeb: your order #{orderId} has been cancelled. If this is unexpected, please contact us.";
}
