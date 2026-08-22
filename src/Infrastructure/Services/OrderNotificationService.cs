using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CatalogItem = Microsoft.eShopWeb.ApplicationCore.Entities.CatalogItem;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private static readonly Address DefaultShipTo =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendIdempotency> _resendKeys;
    private readonly IUriComposer _uriComposer;
    private readonly ITwilioMessageClient _twilio;
    private readonly TwilioOptions _twilioOptions;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendIdempotency> resendKeys,
        IUriComposer uriComposer,
        ITwilioMessageClient twilio,
        IOptions<TwilioOptions> twilioOptions,
        ILogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _resendKeys = resendKeys;
        _uriComposer = uriComposer;
        _twilio = twilio;
        _twilioOptions = twilioOptions.Value;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.");
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new ArgumentException("Quantities must be greater than zero.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipTo, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"eShopOnWeb: Your order #{order.Id} has been placed. Thank you for your purchase.",
            cancellationToken);

        return order;
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
                    ?? throw new OrderNotFoundException();

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"eShopOnWeb: Your order #{order.Id} is on its way.",
            cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: How did the delivery of order #{order.Id} go? We would love your feedback.",
            cancellationToken,
            DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay));
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
                    ?? throw new OrderNotFoundException();

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"eShopOnWeb: Your order #{order.Id} has been cancelled.",
            cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByBuyerSpec(buyerId), cancellationToken);
        await SyncWithProviderAsync(notifications, cancellationToken);
        return orders;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || (!isAdministrator && order.BuyerId != buyerId))
        {
            throw new OrderNotFoundException();
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpec(orderId), cancellationToken);
        await SyncWithProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.");
        }

        var existingKey = await _resendKeys.FirstOrDefaultAsync(
            new ResendIdempotencyByKeySpec(idempotencyKey.Trim()), cancellationToken);
        if (existingKey != null)
        {
            var previous = await _notifications.GetByIdAsync(existingKey.ResultNotificationId, cancellationToken);
            if (previous != null)
            {
                await SyncWithProviderAsync(new[] { previous }, cancellationToken);
                return previous;
            }
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                     ?? throw new NotificationNotFoundException();

        await SyncWithProviderAsync(new[] { source }, cancellationToken);

        if (IsDelivered(source.ProviderStatus))
        {
            throw new NotificationNotResendableException("The message already reached the shopper.");
        }

        if (string.Equals(source.ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotificationNotResendableException("A scheduled message that has not been sent cannot be resent.");
        }

        var destination = await ResolveDestinationAsync(source.BuyerId, cancellationToken);
        if (destination is null)
        {
            throw new NotificationNotResendableException("The shopper has no contact number on file.");
        }

        var body = !source.ContentRedacted && !string.IsNullOrWhiteSpace(source.Body)
            ? source.Body
            : BodyForKind(source.Kind, source.OrderId);

        var resent = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            source.Kind,
            body!,
            destination.Id,
            destination.CanonicalNumber,
            source.Id);

        await SendAndPersistAsync(resent, scheduleAt: null, cancellationToken);

        var record = new NotificationResendIdempotency(idempotencyKey.Trim(), source.Id, resent.Id);
        await _resendKeys.AddAsync(record, cancellationToken);

        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                           ?? throw new NotificationNotFoundException();

        if (!string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            try
            {
                var redacted = await _twilio.RedactBodyAsync(notification.ProviderSid, cancellationToken);
                notification.ApplyProviderState(
                    redacted.Status,
                    redacted.ErrorCode,
                    redacted.ErrorMessage,
                    redacted.Body);
            }
            catch (TwilioRequestException ex)
            {
                _logger.LogWarning(
                    "Failed to redact provider content for notification {NotificationId}, HTTP {StatusCode}, code {TwilioCode}.",
                    notification.Id, ex.HttpStatus, ex.TwilioCode);
                throw;
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromNumber = _twilioOptions.FromNumber;
        var providerMessages = await _twilio.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsWithProviderSidInRangeSpec(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var providerSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ReconciliationEntry>();

        foreach (var provider in providerMessages)
        {
            if (string.IsNullOrWhiteSpace(provider.Sid))
            {
                continue;
            }

            providerSids.Add(provider.Sid);
            if (localBySid.TryGetValue(provider.Sid, out var notification))
            {
                entries.Add(new ReconciliationEntry(
                    notification.Id.ToString(),
                    provider.Sid,
                    "matched",
                    provider.Status,
                    notification.ProviderStatus,
                    provider.DateSent,
                    notification.Kind.ToString()));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    null,
                    provider.Sid,
                    "providerOnly",
                    provider.Status,
                    null,
                    provider.DateSent,
                    null));
            }
        }

        foreach (var notification in local)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderSid) || providerSids.Contains(notification.ProviderSid))
            {
                continue;
            }

            entries.Add(new ReconciliationEntry(
                notification.Id.ToString(),
                notification.ProviderSid,
                "applicationOnly",
                null,
                notification.ProviderStatus,
                null,
                notification.Kind.ToString()));
        }

        return new ReconciliationReport(from, to, fromNumber, entries);
    }

    private async Task TryNotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        CancellationToken cancellationToken,
        DateTimeOffset? scheduleAt = null)
    {
        var destination = await ResolveDestinationAsync(order.BuyerId, cancellationToken);
        if (destination is null)
        {
            _logger.LogInformation(
                "Skipping {Kind} notification for order {OrderId} because the shopper has no contact number on file.",
                kind, order.Id);
            return;
        }

        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            kind,
            body,
            destination.Id,
            destination.CanonicalNumber);

        if (scheduleAt.HasValue)
        {
            notification.MarkScheduled(scheduleAt.Value);
        }

        try
        {
            await SendAndPersistAsync(notification, scheduleAt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Notification {Kind} for order {OrderId} could not be sent; the order operation still succeeded.",
                kind, order.Id);
        }
    }

    private async Task SendAndPersistAsync(
        OrderNotification notification,
        DateTimeOffset? scheduleAt,
        CancellationToken cancellationToken)
    {
        await _notifications.AddAsync(notification, cancellationToken);

        try
        {
            ProviderMessage sent;
            if (scheduleAt.HasValue)
            {
                sent = await _twilio.ScheduleAsync(
                    notification.DestinationNumber, notification.Body!, scheduleAt.Value, cancellationToken);
            }
            else
            {
                sent = await _twilio.SendAsync(
                    notification.DestinationNumber, notification.Body!, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(sent.Sid))
            {
                notification.ApplySendFailure(sent.Status, sent.ErrorCode, sent.ErrorMessage);
            }
            else
            {
                notification.ApplyProviderAcceptance(sent.Sid, sent.Status, sent.ErrorCode, sent.ErrorMessage);
            }
        }
        catch (TwilioRequestException ex)
        {
            notification.ApplySendFailure("failed", ex.TwilioCode?.ToString(), $"HTTP {ex.HttpStatus}");
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogWarning(
                "Provider rejected notification {NotificationId} for order {OrderId} with HTTP {StatusCode}, code {TwilioCode}.",
                notification.Id, notification.OrderId, ex.HttpStatus, ex.TwilioCode);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notification.ApplySendFailure("failed", null, "send_failed");
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogWarning(
                ex,
                "Provider send failed for notification {NotificationId} on order {OrderId}.",
                notification.Id, notification.OrderId);
            return;
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation(
            "Recorded notification {NotificationId} for order {OrderId} as {Status} with provider sid {ProviderSid}.",
            notification.Id, notification.OrderId, notification.ProviderStatus, notification.ProviderSid);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpec(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            try
            {
                var cancelled = await _twilio.CancelScheduledAsync(followUp.ProviderSid!, cancellationToken);
                followUp.ApplyProviderState(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage, cancelled.Body);
                await _notifications.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation(
                    "Cancelled scheduled follow-up {NotificationId} for order {OrderId}; provider status {Status}.",
                    followUp.Id, orderId, cancelled.Status);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}.",
                    followUp.Id, orderId);
            }
        }
    }

    private async Task SyncWithProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var latest = await _twilio.FetchAsync(notification.ProviderSid, cancellationToken);
                if (latest is null)
                {
                    continue;
                }

                notification.ApplyProviderState(latest.Status, latest.ErrorCode, latest.ErrorMessage, latest.Body);
                if (notification.ContentRedacted)
                {
                    notification.RedactContent();
                }

                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Could not refresh provider state for notification {NotificationId}.",
                    notification.Id);
            }
        }
    }

    private async Task<ShopperContactNumber?> ResolveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _contactNumbers.FirstOrDefaultAsync(new LatestContactNumberByBuyerSpec(buyerId), cancellationToken);
    }

    private static bool IsDelivered(string status)
        => string.Equals(status, "delivered", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "read", StringComparison.OrdinalIgnoreCase);

    private static string BodyForKind(OrderNotificationKind kind, int orderId) => kind switch
    {
        OrderNotificationKind.OrderPlaced =>
            $"eShopOnWeb: Your order #{orderId} has been placed. Thank you for your purchase.",
        OrderNotificationKind.OrderDispatched =>
            $"eShopOnWeb: Your order #{orderId} is on its way.",
        OrderNotificationKind.DeliveryFollowUp =>
            $"eShopOnWeb: How did the delivery of order #{orderId} go? We would love your feedback.",
        OrderNotificationKind.OrderCancelled =>
            $"eShopOnWeb: Your order #{orderId} has been cancelled.",
        _ => $"eShopOnWeb: An update for order #{orderId}."
    };
}
