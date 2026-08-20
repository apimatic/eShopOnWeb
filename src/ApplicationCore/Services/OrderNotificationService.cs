using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private static readonly Address DefaultShipTo = new("Not specified", "N/A", "N/A", "N/A", "00000");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendRecord> _resendRecords;
    private readonly ISmsNotificationGateway _sms;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;
    private readonly IOrderNotificationSettings _settings;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendRecord> resendRecords,
        ISmsNotificationGateway sms,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger,
        IOrderNotificationSettings settings)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _resendRecords = resendRecords;
        _sms = sms;
        _uriComposer = uriComposer;
        _logger = logger;
        _settings = settings;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        Address? shipTo,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new InvalidOrderStateException("An order must contain at least one item.");
        }

        foreach (var line in lines)
        {
            Guard.Against.NegativeOrZero(line.CatalogItemId, nameof(line.CatalogItemId));
            Guard.Against.NegativeOrZero(line.Quantity, nameof(line.Quantity));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new InvalidOrderStateException("One or more catalog items were not found.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.png"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrWhiteSpace(pictureUri))
            {
                pictureUri = "images/products/placeholder.png";
            }

            return new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri),
                catalogItem.Price,
                line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipTo ?? DefaultShipTo, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.",
            scheduleFor: null,
            cancellationToken);

        return order;
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderOrThrow(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            scheduleFor: null,
            cancellationToken);

        var followUpAt = DateTimeOffset.UtcNow.AddDays(_settings.FollowUpDelayDays <= 0 ? 3 : _settings.FollowUpDelayDays);
        await TryNotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did the delivery of eShopOnWeb order #{order.Id} go?",
            followUpAt,
            cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderOrThrow(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            scheduleFor: null,
            cancellationToken);

        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpec(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            await TryCancelScheduledAsync(followUp, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        await RefreshNotificationsAsync(orders.Select(o => o.Id).ToArray(), cancellationToken);
        return orders;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListOrderNotificationsAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return Array.Empty<OrderNotification>();
        }

        if (!isAdministrator && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return Array.Empty<OrderNotification>();
        }

        await RefreshNotificationsAsync(new[] { orderId }, cancellationToken);
        return await _notifications.ListAsync(new NotificationsByOrderIdSpec(orderId), cancellationToken);
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _resendRecords.FirstOrDefaultAsync(
            new ResendRecordByKeySpec(notificationId, idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            var prior = await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (prior is not null)
            {
                return prior;
            }
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new EntityNotFoundException("Notification was not found.");

        var body = original.ContentDisposed
            ? FallbackBody(original)
            : (original.Body ?? FallbackBody(original));

        var destination = await ResolveDestinationAsync(original.BuyerId, cancellationToken);
        if (destination is null)
        {
            throw new InvalidOrderStateException("The shopper has no reachable contact number on file.");
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            body,
            destination,
            scheduledForUtc: null,
            resendOfNotificationId: original.Id);

        await _notifications.AddAsync(resend, cancellationToken);
        await DeliverAsync(resend, scheduleFor: null, cancellationToken);

        var record = new NotificationResendRecord(original.Id, idempotencyKey, resend.Id);
        await _resendRecords.AddAsync(record, cancellationToken);
        return resend;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new EntityNotFoundException("Notification was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            try
            {
                var redacted = await _sms.RedactBodyAsync(notification.ProviderSid, cancellationToken);
                notification.ApplyProviderState(
                    redacted.Sid,
                    redacted.Status,
                    redacted.ErrorCode,
                    redacted.ErrorMessage,
                    bodyFromProvider: null);
            }
            catch (Exception ex) when (IsNotificationFailure(ex))
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}: {Message}",
                    notification.Id, SafeError(ex));
                throw new SmsProviderException("The provider could not dispose of the message content.",
                    ex is SmsProviderException sms ? sms.StatusCode : null, ex);
            }
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new InvalidOrderStateException("The reconciliation range end must not precede its start.");
        }

        var providerList = await _sms.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var providerBySid = providerList.Messages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localInRange = await _notifications.ListAsync(
            new NotificationsCreatedBetweenSpec(from, to), cancellationToken);

        var sids = providerBySid.Keys
            .Concat(localInRange.Select(n => n.ProviderSid).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var localBySid = sids.Length == 0
            ? new Dictionary<string, OrderNotification>(StringComparer.Ordinal)
            : (await _notifications.ListAsync(new NotificationsByProviderSidsSpec(sids), cancellationToken))
                .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
                .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var mismatches = new List<ReconciliationMismatch>();

        foreach (var (sid, provider) in providerBySid)
        {
            if (!localBySid.ContainsKey(sid))
            {
                mismatches.Add(new ReconciliationMismatch(
                    null, sid, "provider_only", provider.Status, provider.DateSent));
            }
        }

        foreach (var local in localInRange)
        {
            if (string.IsNullOrEmpty(local.ProviderSid))
            {
                mismatches.Add(new ReconciliationMismatch(
                    local.Id.ToString(), null, "local_only", local.Status, null));
                continue;
            }

            if (!providerBySid.ContainsKey(local.ProviderSid))
            {
                mismatches.Add(new ReconciliationMismatch(
                    local.Id.ToString(), local.ProviderSid, "local_only", local.Status, null));
            }
        }

        var matched = providerBySid.Keys.Count(sid => localBySid.ContainsKey(sid));

        return new ReconciliationReport(
            from,
            to,
            _settings.FromNumber,
            providerBySid.Count,
            localInRange.Count,
            matched,
            providerList.Truncated,
            mismatches);
    }

    private async Task<Order> GetOrderOrThrow(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new EntityNotFoundException("Order was not found.");
        }

        return order;
    }

    private async Task TryNotifyAsync(
        Order order,
        string kind,
        string body,
        DateTimeOffset? scheduleFor,
        CancellationToken cancellationToken)
    {
        try
        {
            var destination = await ResolveDestinationAsync(order.BuyerId, cancellationToken);
            if (destination is null)
            {
                _logger.LogInformation("Skipping {Kind} SMS for order {OrderId}; no contact number on file.", kind, order.Id);
                return;
            }

            var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, destination, scheduleFor);
            await _notifications.AddAsync(notification, cancellationToken);
            await DeliverAsync(notification, scheduleFor, cancellationToken);
        }
        catch (Exception ex) when (IsNotificationFailure(ex))
        {
            _logger.LogWarning("SMS for order {OrderId} kind {Kind} did not send: {Message}", order.Id, kind, SafeError(ex));
        }
    }

    private async Task DeliverAsync(OrderNotification notification, DateTimeOffset? scheduleFor, CancellationToken cancellationToken)
    {
        try
        {
            ProviderMessageResult result;
            if (scheduleFor.HasValue)
            {
                result = await _sms.ScheduleAsync(notification.DestinationNumber, notification.Body ?? string.Empty, scheduleFor.Value, cancellationToken);
            }
            else
            {
                result = await _sms.SendAsync(notification.DestinationNumber, notification.Body ?? string.Empty, cancellationToken);
            }

            notification.ApplyProviderState(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, result.Body);
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogInformation("Recorded provider message {ProviderSid} for notification {NotificationId} order {OrderId}",
                result.Sid ?? string.Empty, notification.Id, notification.OrderId);
        }
        catch (Exception ex) when (IsNotificationFailure(ex))
        {
            notification.MarkSendFailed(SafeError(ex));
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogWarning("Provider send failed for notification {NotificationId} order {OrderId}: {Message}",
                notification.Id, notification.OrderId, SafeError(ex));
        }
    }

    private async Task TryCancelScheduledAsync(OrderNotification followUp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(followUp.ProviderSid))
        {
            return;
        }

        try
        {
            var result = await _sms.CancelScheduledAsync(followUp.ProviderSid, cancellationToken);
            followUp.ApplyProviderState(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, result.Body);
            await _notifications.UpdateAsync(followUp, cancellationToken);
            _logger.LogInformation("Cancelled scheduled follow-up {ProviderSid} for order {OrderId}", followUp.ProviderSid, followUp.OrderId);
        }
        catch (Exception ex) when (IsNotificationFailure(ex))
        {
            try
            {
                var fetched = await _sms.FetchAsync(followUp.ProviderSid, cancellationToken);
                followUp.ApplyProviderState(fetched.Sid, fetched.Status, fetched.ErrorCode, fetched.ErrorMessage, fetched.Body);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception fetchEx) when (IsNotificationFailure(fetchEx))
            {
                _logger.LogWarning("Could not cancel or refresh follow-up {NotificationId} for order {OrderId}: {Message}",
                    followUp.Id, followUp.OrderId, SafeError(ex));
            }
        }
    }

    private async Task RefreshNotificationsAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return;
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdsSpec(orderIds), cancellationToken);
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var fetched = await _sms.FetchAsync(notification.ProviderSid, cancellationToken);
                var body = notification.ContentDisposed ? null : fetched.Body;
                notification.ApplyProviderState(fetched.Sid, fetched.Status, fetched.ErrorCode, fetched.ErrorMessage, body);
                if (notification.ContentDisposed)
                {
                    notification.DisposeContent();
                }

                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex) when (IsNotificationFailure(ex))
            {
                _logger.LogWarning("Could not refresh notification {NotificationId}: {Message}", notification.Id, SafeError(ex));
            }
        }
    }

    private async Task<string?> ResolveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.Number;
    }

    private static string FallbackBody(OrderNotification original) =>
        original.Kind switch
        {
            NotificationKind.OrderPlaced => $"Your eShopOnWeb order #{original.OrderId} has been placed.",
            NotificationKind.OrderDispatched => $"Your eShopOnWeb order #{original.OrderId} is on its way.",
            NotificationKind.DeliveryFollowUp => $"How did the delivery of eShopOnWeb order #{original.OrderId} go?",
            NotificationKind.OrderCancelled => $"Your eShopOnWeb order #{original.OrderId} has been cancelled.",
            _ => $"Update for eShopOnWeb order #{original.OrderId}."
        };

    private static bool IsNotificationFailure(Exception ex) =>
        ex is SmsProviderException
            or System.Net.Http.HttpRequestException
            or TaskCanceledException
            or System.Text.Json.JsonException
            or OperationCanceledException;

    private static string SafeError(Exception ex)
    {
        if (ex is SmsProviderException sms)
        {
            return sms.StatusCode is null
                ? "provider error"
                : $"provider HTTP {(int)sms.StatusCode.Value}";
        }

        return ex.GetType().Name;
    }
}
