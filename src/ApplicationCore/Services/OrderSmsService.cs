using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderSmsService : IOrderSmsService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderSmsService> _logger;

    public OrderSmsService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioMessagingClient messaging,
        IUriComposer uriComposer,
        IAppLogger<OrderSmsService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _messaging = messaging;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Result<Order>> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogLine> lines,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateLines(lines);
        if (!validation.IsSuccess)
        {
            return Result<Order>.Invalid(validation.ValidationErrors.ToList());
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            return Result<Order>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "items", ErrorMessage = "One or more catalog items were not found." }
            });
        }

        var address = shipToAddress ?? new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, address, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderPlaced,
            BuildBody(NotificationKind.OrderPlaced, order),
            sendAt: null,
            cancellationToken);

        return Result<Order>.Success(order);
    }

    public async Task<Result<Order>> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            return Result<Order>.NotFound();
        }

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            return Result<Order>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "status", ErrorMessage = ex.Message }
            });
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            BuildBody(NotificationKind.OrderDispatched, order),
            sendAt: null,
            cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            BuildBody(NotificationKind.DeliveryFollowUp, order),
            sendAt: DateTimeOffset.UtcNow.Add(FollowUpDelay),
            cancellationToken);

        return Result<Order>.Success(order);
    }

    public async Task<Result<Order>> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            return Result<Order>.NotFound();
        }

        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOperationException ex)
        {
            return Result<Order>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "status", ErrorMessage = ex.Message }
            });
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderCancelled,
            BuildBody(NotificationKind.OrderCancelled, order),
            sendAt: null,
            cancellationToken);

        return Result<Order>.Success(order);
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<Result<IReadOnlyList<OrderNotification>>> ListOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order == null || order.BuyerId != buyerId)
        {
            return Result<IReadOnlyList<OrderNotification>>.NotFound();
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpec(orderId), cancellationToken);
        await SyncWithProviderAsync(notifications, cancellationToken);
        return Result<IReadOnlyList<OrderNotification>>.Success(notifications);
    }

    public async Task<Result<OrderNotification>> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result<OrderNotification>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "idempotencyKey", ErrorMessage = "An idempotency key is required." }
            });
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            return Result<OrderNotification>.NotFound();
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new NotificationByResendKeySpec(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (existing != null)
        {
            await SyncWithProviderAsync(new[] { existing }, cancellationToken);
            return Result<OrderNotification>.Success(existing);
        }

        await SyncWithProviderAsync(new[] { original }, cancellationToken);

        if (!original.CanResend)
        {
            return Result<OrderNotification>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "notification", ErrorMessage = "This notification cannot be re-sent in its current delivery state." }
            });
        }

        if (string.IsNullOrWhiteSpace(original.Body))
        {
            return Result<OrderNotification>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "notification", ErrorMessage = "The original message body is no longer available to re-send." }
            });
        }

        var destinationStillOnFile = await DestinationStillRegisteredAsync(original.BuyerId, original.DestinationE164, cancellationToken);
        if (!destinationStillOnFile)
        {
            return Result<OrderNotification>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "notification", ErrorMessage = "The destination is no longer on file for this shopper." }
            });
        }

        var resent = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            original.DestinationE164,
            original.Body,
            scheduledSendAt: null,
            resentFromNotificationId: original.Id,
            resendIdempotencyKey: idempotencyKey.Trim());

        await _notifications.AddAsync(resent, cancellationToken);
        await DispatchToProviderAsync(resent, sendAt: null, cancellationToken);
        return Result<OrderNotification>.Success(resent);
    }

    public async Task<Result> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return Result.NotFound();
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var updated = await _messaging.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(updated.Status, updated.ErrorCode, updated.Body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to redact provider content for notification {NotificationId}: {Message}",
                    notification.Id,
                    LogSanitizer.RedactPhoneNumbers(ex.Message));
                return Result.Error("The provider could not dispose of the message content.");
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ReconciliationReport>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            return Result<ReconciliationReport>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "to", ErrorMessage = "'to' must be on or after 'from'." }
            });
        }

        IReadOnlyList<ProviderMessage> providerMessages;
        try
        {
            providerMessages = await _messaging.ListFromNumberAsync(from, to, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Reconciliation listing failed: {Message}", LogSanitizer.RedactPhoneNumbers(ex.Message));
            return Result<ReconciliationReport>.Error("The provider message list could not be retrieved.");
        }

        var applicationRecords = await _notifications.ListAsync(new NotificationsInCreatedRangeSpec(from, to), cancellationToken);
        await SyncWithProviderAsync(applicationRecords, cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var applicationBySid = applicationRecords
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationRow>();
        var providerOnly = new List<ReconciliationRow>();
        var applicationOnly = new List<ReconciliationRow>();

        foreach (var pair in providerBySid)
        {
            if (applicationBySid.TryGetValue(pair.Key, out var local))
            {
                matched.Add(new ReconciliationRow(
                    local.Id.ToString(),
                    pair.Key,
                    local.ProviderStatus,
                    pair.Value.Status,
                    "matched"));
            }
            else
            {
                providerOnly.Add(new ReconciliationRow(
                    null,
                    pair.Key,
                    null,
                    pair.Value.Status,
                    "providerOnly"));
            }
        }

        foreach (var local in applicationRecords)
        {
            if (string.IsNullOrEmpty(local.ProviderMessageSid))
            {
                applicationOnly.Add(new ReconciliationRow(
                    local.Id.ToString(),
                    null,
                    local.ProviderStatus,
                    null,
                    "applicationOnly"));
                continue;
            }

            if (!providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                applicationOnly.Add(new ReconciliationRow(
                    local.Id.ToString(),
                    local.ProviderMessageSid,
                    local.ProviderStatus,
                    null,
                    "applicationOnly"));
            }
        }

        return Result<ReconciliationReport>.Success(new ReconciliationReport(
            from,
            to,
            _messaging.FromNumber,
            matched,
            providerOnly,
            applicationOnly));
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(
        IReadOnlyList<int> orderIds,
        CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdsSpec(orderIds.ToArray()), cancellationToken);
        await SyncWithProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    private static Result ValidateLines(IReadOnlyList<CatalogLine> lines)
    {
        if (lines == null || lines.Count == 0)
        {
            return Result.Invalid(new List<ValidationError>
            {
                new() { Identifier = "items", ErrorMessage = "At least one catalog item is required." }
            });
        }

        if (lines.Any(l => l.CatalogItemId <= 0 || l.Quantity <= 0))
        {
            return Result.Invalid(new List<ValidationError>
            {
                new() { Identifier = "items", ErrorMessage = "Each item must have a positive catalogItemId and quantity." }
            });
        }

        return Result.Success();
    }

    private static string BuildBody(NotificationKind kind, Order order)
    {
        return kind switch
        {
            NotificationKind.OrderPlaced =>
                $"eShopOnWeb: order #{order.Id} has been placed. Total {order.Total():0.00}.",
            NotificationKind.OrderDispatched =>
                $"eShopOnWeb: order #{order.Id} is on its way.",
            NotificationKind.DeliveryFollowUp =>
                $"eShopOnWeb: how did the delivery of order #{order.Id} go?",
            NotificationKind.OrderCancelled =>
                $"eShopOnWeb: order #{order.Id} has been cancelled.",
            _ => $"eShopOnWeb: an update for order #{order.Id}."
        };
    }

    private async Task<ContactNumber?> GetLatestContactAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpec(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    private async Task<bool> DestinationStillRegisteredAsync(string buyerId, string e164, CancellationToken cancellationToken)
    {
        var match = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpec(buyerId, e164), cancellationToken);
        return match != null;
    }

    private async Task TryNotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var contact = await GetLatestContactAsync(order.BuyerId, cancellationToken);
            if (contact == null)
            {
                return;
            }

            var notification = new OrderNotification(order.Id, order.BuyerId, kind, contact.PhoneNumber, body, sendAt);
            await _notifications.AddAsync(notification, cancellationToken);
            await DispatchToProviderAsync(notification, sendAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Notification {Kind} for order {OrderId} could not be processed: {Message}",
                kind,
                order.Id,
                LogSanitizer.RedactPhoneNumbers(ex.Message));
        }
    }

    private async Task DispatchToProviderAsync(
        OrderNotification notification,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var sent = await _messaging.SendAsync(notification.DestinationE164, notification.Body ?? string.Empty, sendAt, cancellationToken);
            notification.RecordProviderAcceptance(sent.Sid, sent.Status);
            if (sent.ErrorCode.HasValue)
            {
                notification.ApplyProviderState(sent.Status, sent.ErrorCode, sent.Body);
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure("The provider did not accept the message.");
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogWarning(
                "Provider send failed for notification {NotificationId} (order {OrderId}): {Message}",
                notification.Id,
                notification.OrderId,
                LogSanitizer.RedactPhoneNumbers(ex.Message));
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderIdSpec(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                followUp.ApplyProviderState("canceled", null, followUp.Body);
                await _notifications.UpdateAsync(followUp, cancellationToken);
                continue;
            }

            try
            {
                var updated = await _messaging.CancelAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.ApplyProviderState(updated.Status, updated.ErrorCode, updated.Body);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not cancel scheduled follow-up {NotificationId} for order {OrderId}: {Message}",
                    followUp.Id,
                    orderId,
                    LogSanitizer.RedactPhoneNumbers(ex.Message));
            }
        }
    }

    private async Task SyncWithProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || notification.IsTerminalProviderStatus)
            {
                continue;
            }

            try
            {
                var current = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(current.Status, current.ErrorCode, notification.ContentRedacted ? null : current.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not refresh provider status for notification {NotificationId}: {Message}",
                    notification.Id,
                    LogSanitizer.RedactPhoneNumbers(ex.Message));
            }
        }
    }
}
