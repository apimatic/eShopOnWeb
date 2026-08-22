using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationWorkflow : IOrderNotificationWorkflow
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private static readonly Address ApiShipToAddress = new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ResendIdempotencyRecord> _resendKeys;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationWorkflow> _logger;

    public OrderNotificationWorkflow(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<ResendIdempotencyRecord> resendKeys,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationWorkflow> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _resendKeys = resendKeys;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, CancellationToken cancellationToken = default)
    {
        if (lines == null || lines.Count == 0)
        {
            return FailPlace(400, "At least one catalog item is required.");
        }

        if (lines.Any(l => l.CatalogItemId <= 0 || l.Quantity <= 0))
        {
            return FailPlace(400, "Each item must include a catalogItemId and a quantity greater than zero.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            return FailPlace(400, "One or more catalog items were not found.");
        }

        var itemsById = catalogItems.ToDictionary(c => c.Id);
        var orderItems = lines.Select(line =>
        {
            var catalogItem = itemsById[line.CatalogItemId];
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(ordered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, ApiShipToAddress, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderPlaced,
            $"eShopOnWeb: your order #{order.Id} has been placed. Thank you!",
            schedule: false,
            cancellationToken);

        return new PlaceOrderResult { Succeeded = true, StatusCode = 201, Order = order };
    }

    public async Task<OrderLifecycleResult> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            return FailLifecycle(404, "Order not found.");
        }

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            return FailLifecycle(409, ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            $"eShopOnWeb: order #{order.Id} is on its way.",
            schedule: false,
            cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: how did delivery of order #{order.Id} go?",
            schedule: true,
            cancellationToken);

        return new OrderLifecycleResult { Succeeded = true, StatusCode = 200, Order = order };
    }

    public async Task<OrderLifecycleResult> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            return FailLifecycle(404, "Order not found.");
        }

        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOperationException ex)
        {
            return FailLifecycle(409, ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderCancelled,
            $"eShopOnWeb: order #{order.Id} has been cancelled.",
            schedule: false,
            cancellationToken);

        return new OrderLifecycleResult { Succeeded = true, StatusCode = 200, Order = order };
    }

    public async Task<IReadOnlyList<ShopperOrderSummary>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<ShopperOrderSummary>();
        }

        var notifications = await _notifications.ListAsync(
            new NotificationsByBuyerOrdersSpecification(orders.Select(o => o.Id)), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
        return orders
            .OrderByDescending(o => o.Id)
            .Select(order => new ShopperOrderSummary
            {
                Order = order,
                Notifications = byOrder.TryGetValue(order.Id, out var list) ? list : Array.Empty<OrderNotification>()
            })
            .ToList();
    }

    public async Task<OrderNotificationsResult> ListNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null || order.BuyerId != buyerId)
        {
            return new OrderNotificationsResult { Succeeded = false, StatusCode = 404, Error = "Order not found." };
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);

        return new OrderNotificationsResult
        {
            Succeeded = true,
            StatusCode = 200,
            Order = order,
            Notifications = notifications
        };
    }

    public async Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return new ResendNotificationResult { Succeeded = false, StatusCode = 400, Error = "idempotencyKey is required." };
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source == null)
        {
            return new ResendNotificationResult { Succeeded = false, StatusCode = 404, Error = "Notification not found." };
        }

        var existingKey = await _resendKeys.FirstOrDefaultAsync(
            new ResendIdempotencySpecification(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (existingKey != null)
        {
            var previous = await _notifications.GetByIdAsync(existingKey.ResultNotificationId, cancellationToken);
            if (previous != null)
            {
                await RefreshStatusesAsync(new[] { previous }, cancellationToken);
                return new ResendNotificationResult { Succeeded = true, StatusCode = 200, Notification = previous };
            }
        }

        await RefreshStatusesAsync(new[] { source }, cancellationToken);

        if (source.HasReachedShopper())
        {
            return new ResendNotificationResult { Succeeded = false, StatusCode = 409, Error = "The original message already reached the shopper." };
        }

        if (string.Equals(source.Status, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            return new ResendNotificationResult { Succeeded = false, StatusCode = 409, Error = "A cancelled message cannot be resent." };
        }

        if (!source.IsTerminalFailure() && !string.Equals(source.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return new ResendNotificationResult
            {
                Succeeded = false,
                StatusCode = 409,
                Error = $"The original message is still {source.Status} and has not failed delivery."
            };
        }

        if (source.ContentRedacted || string.IsNullOrEmpty(source.Body))
        {
            return new ResendNotificationResult { Succeeded = false, StatusCode = 409, Error = "The original message content is no longer available." };
        }

        var destinationStillOnFile = await DestinationStillRegisteredAsync(source.BuyerId, source.DestinationNumber, cancellationToken);
        if (!destinationStillOnFile)
        {
            return new ResendNotificationResult { Succeeded = false, StatusCode = 409, Error = "The destination number is no longer on file." };
        }

        var resend = new OrderNotification(source.OrderId, source.BuyerId, source.Kind, source.Body, source.DestinationNumber, isScheduled: false);
        resend.MarkAsResendOf(source.Id);
        await _notifications.AddAsync(resend, cancellationToken);

        var send = await _smsGateway.SendAsync(new SmsSendCommand { To = source.DestinationNumber, Body = source.Body }, cancellationToken);
        ApplySendResult(resend, send);
        await _notifications.UpdateAsync(resend, cancellationToken);

        await _resendKeys.AddAsync(new ResendIdempotencyRecord(source.Id, idempotencyKey.Trim(), resend.Id), cancellationToken);

        return new ResendNotificationResult { Succeeded = true, StatusCode = 201, Notification = resend };
    }

    public async Task<NotificationContentResult> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return new NotificationContentResult { Succeeded = false, StatusCode = 404, Error = "Notification not found." };
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var redact = await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            if (!redact.Succeeded)
            {
                return new NotificationContentResult
                {
                    Succeeded = false,
                    StatusCode = 502,
                    Error = PhoneNumberSanitizer.Redact(redact.Error) ?? "The provider could not dispose of the message content."
                };
            }

            if (redact.Message != null)
            {
                notification.ApplyProviderState(redact.Message.Status, redact.Message.ErrorCode, redact.Message.ErrorMessage);
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return new NotificationContentResult { Succeeded = true, StatusCode = 204 };
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            return new ReconciliationReport { Succeeded = false, StatusCode = 400, Error = "'to' must be on or after 'from'." };
        }

        var list = await _smsGateway.ListFromConfiguredSenderAsync(from, to, cancellationToken);
        if (!list.Succeeded)
        {
            return new ReconciliationReport
            {
                Succeeded = false,
                StatusCode = 502,
                Error = PhoneNumberSanitizer.Redact(list.Error) ?? "The provider could not list messages for reconciliation."
            };
        }

        var report = await BuildReconciliationAsync(from, to, list.Messages, cancellationToken);
        return new ReconciliationReport
        {
            Succeeded = report.Succeeded,
            StatusCode = report.StatusCode,
            Error = report.Error,
            From = report.From,
            To = report.To,
            FromNumber = list.FromNumber,
            Matched = report.Matched,
            ProviderOnly = report.ProviderOnly,
            ApplicationOnly = report.ApplicationOnly
        };
    }

    private async Task<ReconciliationReport> BuildReconciliationAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<SmsMessageSnapshot> providerMessages,
        CancellationToken cancellationToken)
    {
        var local = await _notifications.ListAsync(new NotificationsCreatedInRangeSpecification(from, to), cancellationToken);
        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationItem>();
        var providerOnly = new List<ReconciliationItem>();
        var applicationOnly = new List<ReconciliationItem>();

        foreach (var provider in providerBySid.Values)
        {
            if (localBySid.TryGetValue(provider.Sid, out var notification))
            {
                matched.Add(ToItem(notification, provider));
            }
            else
            {
                providerOnly.Add(new ReconciliationItem
                {
                    ProviderMessageSid = provider.Sid,
                    Status = provider.Status,
                    ProviderDate = provider.DateSent ?? provider.DateCreated
                });
            }
        }

        foreach (var notification in local)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || !providerBySid.ContainsKey(notification.ProviderMessageSid))
            {
                applicationOnly.Add(ToItem(notification, null));
            }
        }

        return new ReconciliationReport
        {
            Succeeded = true,
            StatusCode = 200,
            From = from,
            To = to,
            Matched = matched,
            ProviderOnly = providerOnly,
            ApplicationOnly = applicationOnly
        };
    }

    private async Task TryNotifyAsync(Order order, NotificationKind kind, string body, bool schedule, CancellationToken cancellationToken)
    {
        var destination = await GetActiveDestinationAsync(order.BuyerId, cancellationToken);
        if (destination == null)
        {
            _logger.LogInformation("Skipping {Kind} SMS for order {OrderId}; no contact number on file.", kind, order.Id);
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, destination, schedule);
        if (schedule)
        {
            notification.MarkScheduledFor(DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay));
        }

        await _notifications.AddAsync(notification, cancellationToken);

        SmsOperationResult result;
        try
        {
            var command = new SmsSendCommand { To = destination, Body = body };
            result = schedule && notification.ScheduledFor.HasValue
                ? await _smsGateway.ScheduleAsync(command, notification.ScheduledFor.Value, cancellationToken)
                : await _smsGateway.SendAsync(command, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SMS {Kind} for order {OrderId} threw: {Reason}", kind, order.Id, PhoneNumberSanitizer.Redact(ex.Message));
            notification.RecordLocalSendFailure(PhoneNumberSanitizer.Redact(ex.Message));
            await _notifications.UpdateAsync(notification, cancellationToken);
            return;
        }

        ApplySendResult(notification, result);
        await _notifications.UpdateAsync(notification, cancellationToken);
        if (!result.Succeeded)
        {
            _logger.LogWarning("SMS {Kind} for order {OrderId} was not accepted: {Reason}", kind, order.Id, PhoneNumberSanitizer.Redact(result.Error));
        }
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var scheduled = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var notification in scheduled)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            var cancel = await _smsGateway.CancelScheduledAsync(notification.ProviderMessageSid, cancellationToken);
            if (cancel.Succeeded && cancel.Message != null)
            {
                notification.ApplyProviderState(cancel.Message.Status, cancel.Message.ErrorCode, cancel.Message.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Could not cancel follow-up {NotificationId} for order {OrderId}: {Reason}",
                    notification.Id, orderId, PhoneNumberSanitizer.Redact(cancel.Error));
            }
        }
    }

    private async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            if (notification.HasReachedShopper() || notification.IsTerminalFailure()
                || string.Equals(notification.Status, "canceled", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fetch = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            if (fetch.Succeeded && fetch.Message != null)
            {
                notification.ApplyProviderState(fetch.Message.Status, fetch.Message.ErrorCode, fetch.Message.ErrorMessage);
                if (notification.ContentRedacted)
                {
                    notification.RedactContent();
                }
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
        }
    }

    private async Task<string?> GetActiveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.CanonicalNumber;
    }

    private async Task<bool> DestinationStillRegisteredAsync(string buyerId, string destination, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.Any(n => n.CanonicalNumber == destination);
    }

    private static void ApplySendResult(OrderNotification notification, SmsOperationResult result)
    {
        if (result.Succeeded && result.Message != null)
        {
            notification.RecordProviderAcceptance(
                result.Message.Sid,
                result.Message.Status,
                result.Message.ErrorCode,
                result.Message.ErrorMessage);
            return;
        }

        notification.RecordLocalSendFailure(PhoneNumberSanitizer.Redact(result.Error) ?? "Send failed.");
    }

    private static ReconciliationItem ToItem(OrderNotification notification, SmsMessageSnapshot? provider)
    {
        return new ReconciliationItem
        {
            NotificationId = notification.Id,
            ProviderMessageSid = notification.ProviderMessageSid ?? provider?.Sid,
            Status = provider?.Status ?? notification.Status,
            Kind = notification.Kind.ToString(),
            OrderId = notification.OrderId,
            ProviderDate = provider?.DateSent ?? provider?.DateCreated ?? notification.CreatedAt
        };
    }

    private static PlaceOrderResult FailPlace(int status, string error) =>
        new() { Succeeded = false, StatusCode = status, Error = error };

    private static OrderLifecycleResult FailLifecycle(int status, string error) =>
        new() { Succeeded = false, StatusCode = status, Error = error };
}
