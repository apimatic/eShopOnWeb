using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places orders and drives the SMS notifications that go out as an order moves. A message that cannot be sent
/// is recorded (SendFailed) but never fails the underlying order operation; a shopper with no number on file is
/// simply not messaged. The local notification row is always written before the provider call and completed
/// after it, so a transport failure leaves a row reconciliation can line up against the provider.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly ISmsGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        IRepository<CatalogItem> itemRepository,
        ISmsGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _itemRepository = itemRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderRequestItem> items, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be positive.", nameof(items));
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Unknown catalog item id {line.CatalogItemId}.", nameof(items));

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        // Reuse the existing order/order-item model. No shipping address is carried by this API, and Order
        // requires one, so a placeholder is used (address is out of this feature's scope).
        var shipToAddress = new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, ct);

        await NotifyAsync(order, NotificationKind.OrderPlaced, PlacedBody(order.Id), ct);
        return order.Id;
    }

    public async Task<bool> DispatchAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct)
            ?? throw new OrderNotFoundException(orderId);

        // Gate every outbound effect on the transition actually happening (Placed -> Dispatched).
        if (!order.MarkDispatched())
        {
            return false;
        }
        await _orderRepository.UpdateAsync(order, ct);

        await NotifyAsync(order, NotificationKind.OrderDispatched, DispatchedBody(order.Id), ct);
        // The "how did delivery go?" follow-up is queued with the provider for a few days later.
        await NotifyAsync(order, NotificationKind.DeliveryFollowUp, FollowUpBody(order.Id), ct);
        return true;
    }

    public async Task<bool> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct)
            ?? throw new OrderNotFoundException(orderId);

        if (!order.MarkCancelled())
        {
            return false;
        }
        await _orderRepository.UpdateAsync(order, ct);

        // Call off any not-yet-sent follow-up BEFORE telling the shopper, so a "how did delivery go?" for a
        // cancelled order can never slip out.
        var followUps = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), ct);
        foreach (var followUp in followUps)
        {
            var outcome = DeliveryOutcomeMapper.FromProviderStatus(followUp.ProviderStatus, followUp.ContentDisposed);
            if (DeliveryOutcomeMapper.IsTerminal(outcome))
            {
                continue; // already delivered/failed/canceled — nothing to call off
            }

            try
            {
                await _gateway.CancelScheduledAsync(followUp.ProviderMessageSid!, ct);
                followUp.UpdateProviderState("canceled", null, null, null);
                await _notificationRepository.UpdateAsync(followUp, ct);
            }
            catch (SmsGatewayException ex)
            {
                _logger.LogWarning($"Could not cancel follow-up {followUp.Id} for order {orderId}: {ex.Message}");
            }
        }

        await NotifyAsync(order, NotificationKind.OrderCancelled, CancelledBody(order.Id), ct);
        return true;
    }

    public async Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var summaries = new List<OrderSummary>();
        foreach (var order in orders)
        {
            var notifications = await GetAndRefreshNotificationsAsync(order.Id, ct);
            summaries.Add(new OrderSummary(order.Id, order.Status.ToString(), order.OrderDate, order.Total(), notifications));
        }
        return summaries;
    }

    public async Task<IReadOnlyList<NotificationView>> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // A shopper must never see another's order.
            throw new OrderNotFoundException(orderId);
        }
        return await GetAndRefreshNotificationsAsync(orderId, ct);
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        // A prior resend under this key already produced a notification — return it, send nothing again.
        var priorByKey = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (priorByKey is not null)
        {
            return new ResendResult(priorByKey.Id, true, priorByKey.ProviderMessageSid, OutcomeOf(priorByKey));
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct)
            ?? throw new NotificationNotFoundException(notificationId);

        var body = original.Body ?? ResendBody(original.OrderId);
        var resend = new OrderNotification(original.OrderId, original.OwnerId, NotificationKind.Resend,
            original.ToNumber, body, idempotencyKey);

        // Claim the key by writing the row (before the provider call). On SQL Server the unique index on
        // IdempotencyKey rejects a concurrent duplicate; we learn of it by the write failing, then re-read by
        // key and treat the already-stored row as the winner rather than sending a second message.
        try
        {
            await _notificationRepository.AddAsync(resend, ct);
        }
        catch (Exception)
        {
            var winner = await _notificationRepository.FirstOrDefaultAsync(
                new NotificationByIdempotencyKeySpecification(idempotencyKey), ct);
            if (winner is not null && winner.Id != resend.Id)
            {
                return new ResendResult(winner.Id, true, winner.ProviderMessageSid, OutcomeOf(winner));
            }
            throw; // not a duplicate-claim rejection — a genuine failure
        }

        try
        {
            var sent = await _gateway.SendAsync(original.ToNumber, body, ct);
            resend.RecordSent(sent.Sid, sent.Status, sent.ErrorCode, sent.ErrorMessage, sent.DateSent);
        }
        catch (SmsGatewayException ex)
        {
            resend.RecordSendFailure(ex.Message);
            _logger.LogWarning($"Resend {resend.Id} for order {original.OrderId} could not be sent: {ex.Message}");
        }
        await _notificationRepository.UpdateAsync(resend, ct);
        return new ResendResult(resend.Id, false, resend.ProviderMessageSid, OutcomeOf(resend));
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct)
            ?? throw new NotificationNotFoundException(notificationId);

        // Dispose at the provider first. If that fails, the exception surfaces and we do NOT mark disposed
        // locally — the content must be gone at the provider too, not merely hidden by this app.
        if (notification.ProviderMessageSid is not null && !notification.ContentDisposed)
        {
            await _gateway.RedactBodyAsync(notification.ProviderMessageSid, ct);
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification, ct);
        _logger.LogInformation($"Disposed content of notification {notificationId}.");
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        // Ask the provider only for this application's sending number, over the window.
        var providerList = await _gateway.ListSentAsync(from, to, ct);
        // Line up the local side on the SAME clock — the provider's date_sent — not on local row-creation time.
        var localList = await _notificationRepository.ListAsync(new NotificationsByProviderDateSentSpecification(from, to), ct);

        var localBySid = localList
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = new HashSet<string>(providerList.Messages.Where(m => m.Sid is not null).Select(m => m.Sid!));

        var entries = new List<ReconciliationEntry>();
        int matched = 0, providerOnly = 0, eshopOnly = 0;

        foreach (var m in providerList.Messages)
        {
            if (m.Sid is null) continue;
            if (localBySid.TryGetValue(m.Sid, out var local))
            {
                matched++;
                entries.Add(new ReconciliationEntry(m.Sid, "Matched", m.Status, m.DateSent, local.Id, local.Kind.ToString()));
            }
            else
            {
                providerOnly++;
                entries.Add(new ReconciliationEntry(m.Sid, "ProviderOnly", m.Status, m.DateSent, null, null));
            }
        }

        foreach (var local in localList)
        {
            if (local.ProviderMessageSid is null || providerSids.Contains(local.ProviderMessageSid)) continue;
            eshopOnly++;
            entries.Add(new ReconciliationEntry(local.ProviderMessageSid, "EShopOnly", local.ProviderStatus,
                local.ProviderDateSent, local.Id, local.Kind.ToString()));
        }

        return new ReconciliationReport(from, to, _gateway.FromNumber,
            ProviderCount: providerList.Messages.Count,
            EShopCount: localBySid.Count,
            MatchedCount: matched,
            ProviderOnlyCount: providerOnly,
            EShopOnlyCount: eshopOnly,
            Truncated: providerList.Truncated,
            PagesFetched: providerList.PagesFetched,
            Entries: entries);
    }

    /// <summary>Send one message per registered contact number, recording a failure rather than throwing.</summary>
    private async Task NotifyAsync(Order order, NotificationKind kind, string body, CancellationToken ct)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), ct);
        if (numbers.Count == 0)
        {
            _logger.LogInformation($"Order {order.Id}: no contact number on file, no {kind} message sent.");
            return;
        }

        foreach (var contactNumber in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, kind, contactNumber.PhoneNumber, body);
            await _notificationRepository.AddAsync(notification, ct); // local row BEFORE the provider call

            try
            {
                var sent = kind == NotificationKind.DeliveryFollowUp
                    ? await _gateway.ScheduleFollowUpAsync(contactNumber.PhoneNumber, body, ct)
                    : await _gateway.SendAsync(contactNumber.PhoneNumber, body, ct);
                notification.RecordSent(sent.Sid, sent.Status, sent.ErrorCode, sent.ErrorMessage, sent.DateSent);
            }
            catch (SmsGatewayException ex)
            {
                // A message that cannot be sent must never fail the order operation.
                notification.RecordSendFailure(ex.Message);
                _logger.LogWarning($"Notification {notification.Id} ({kind}) for order {order.Id} could not be sent: {ex.Message}");
            }

            await _notificationRepository.UpdateAsync(notification, ct);
        }
    }

    private async Task<IReadOnlyList<NotificationView>> GetAndRefreshNotificationsAsync(int orderId, CancellationToken ct)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        var views = new List<NotificationView>(notifications.Count);
        foreach (var n in notifications)
        {
            // Refresh the provider's delivery outcome for messages still in flight (there is no webhook — we
            // ask the provider). Terminal or disposed messages are left as-is.
            if (n.ProviderMessageSid is not null && !n.ContentDisposed
                && !DeliveryOutcomeMapper.IsTerminal(DeliveryOutcomeMapper.FromProviderStatus(n.ProviderStatus)))
            {
                try
                {
                    var state = await _gateway.FetchStateAsync(n.ProviderMessageSid, ct);
                    n.UpdateProviderState(state.Status, state.ErrorCode, state.ErrorMessage, state.DateSent);
                    await _notificationRepository.UpdateAsync(n, ct);
                }
                catch (SmsGatewayException ex)
                {
                    _logger.LogWarning($"Could not refresh notification {n.Id}: {ex.Message}");
                }
            }
            views.Add(ToView(n));
        }
        return views;
    }

    private static NotificationView ToView(OrderNotification n) => new(
        n.Id, n.OrderId, n.Kind.ToString(), n.ProviderMessageSid, n.ProviderStatus,
        DeliveryOutcomeMapper.FromProviderStatus(n.ProviderStatus, n.ContentDisposed),
        n.ProviderErrorCode, n.SendFailed, n.ContentDisposed, n.ProviderDateSent, n.CreatedAt);

    private static DeliveryOutcome OutcomeOf(OrderNotification n) =>
        DeliveryOutcomeMapper.FromProviderStatus(n.ProviderStatus, n.ContentDisposed);

    private static string PlacedBody(int orderId) => $"Your eShop order #{orderId} has been placed. Thank you!";
    private static string DispatchedBody(int orderId) => $"Good news! Your eShop order #{orderId} is on its way.";
    private static string FollowUpBody(int orderId) => $"How did the delivery of your eShop order #{orderId} go? We'd love your feedback.";
    private static string CancelledBody(int orderId) => $"Your eShop order #{orderId} has been cancelled. Please contact us with any questions.";
    private static string ResendBody(int orderId) => $"An update about your eShop order #{orderId}.";
}
