using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
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
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsProvider _smsProvider;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsProvider smsProvider,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsProvider = smsProvider;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Result<Order>> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items == null || items.Count == 0)
        {
            return AppResults.Invalid<Order>("items", "At least one catalog item is required.");
        }

        if (items.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            return AppResults.Invalid<Order>("items", "Each item must include a catalog item id and a positive quantity.");
        }

        var grouped = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToArray();

        var catalogItems = await _catalogItems.ListAsync(
            new CatalogItemsSpecification(grouped.Select(g => g.CatalogItemId).ToArray()),
            cancellationToken);

        if (catalogItems.Count != grouped.Length)
        {
            return Result<Order>.NotFound();
        }

        var orderItems = grouped.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri)
                ? "placeholder"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(
            buyerId,
            new Address("123 Main St.", "Kent", "OH", "United States", "44240"),
            orderItems);

        await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"eShopOnWeb: your order #{order.Id} has been placed. Thanks for shopping with us.",
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

        order.MarkDispatched();

        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"eShopOnWeb: order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        var followUpAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await TryNotifyAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: how did delivery go for order #{order.Id}? Reply with any feedback.",
            followUpAt,
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

        order.MarkCancelled();

        await _orders.UpdateAsync(order, cancellationToken);

        var existing = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), cancellationToken);
        await RefreshProviderStateAsync(existing, cancellationToken);

        foreach (var followUp in existing.Where(n => n.IsScheduledFollowUp()))
        {
            try
            {
                var snapshot = await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                if (snapshot != null)
                {
                    followUp.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.Body);
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}: {Message}",
                    followUp.Id, order.Id, ex.Message);
            }
        }

        await TryNotifyAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"eShopOnWeb: order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);

        return Result<Order>.Success(order);
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrdersAsync(
        IEnumerable<int> orderIds,
        CancellationToken cancellationToken = default)
    {
        var ids = orderIds.ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdsSpecification(ids), cancellationToken);
        await RefreshProviderStateAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<Result<IReadOnlyList<OrderNotification>>> ListNotificationsAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            return Result<IReadOnlyList<OrderNotification>>.NotFound();
        }

        if (!isAdministrator && order.BuyerId != buyerId)
        {
            return Result<IReadOnlyList<OrderNotification>>.NotFound();
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshProviderStateAsync(notifications, cancellationToken);
        return Result<IReadOnlyList<OrderNotification>>.Success(notifications);
    }

    public async Task<Result<OrderNotification>> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return AppResults.Invalid<OrderNotification>("idempotencyKey", "An idempotency key is required.");
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            return Result<OrderNotification>.NotFound();
        }

        var existingResend = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByResendKeySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existingResend != null)
        {
            await RefreshProviderStateAsync(new[] { existingResend }, cancellationToken);
            return Result<OrderNotification>.Success(existingResend);
        }

        if (!string.IsNullOrEmpty(original.ProviderMessageSid))
        {
            try
            {
                var snapshot = await _smsProvider.FetchAsync(original.ProviderMessageSid, cancellationToken);
                if (snapshot != null)
                {
                    original.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, original.ContentRedacted ? null : snapshot.Body);
                    await _notifications.UpdateAsync(original, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh notification {NotificationId} before resend: {Message}", original.Id, ex.Message);
            }
        }

        if (!original.DidNotReachShopper())
        {
            throw new InvalidOrderStateException("This message reached the shopper and cannot be resent.");
        }

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            throw new InvalidOrderStateException("The message content has been disposed of and cannot be resent.");
        }

        var contactStillOnFile = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(original.BuyerId, original.DestinationNumber),
            cancellationToken);
        if (contactStillOnFile == null)
        {
            throw new InvalidOrderStateException("The destination number is no longer on file for this shopper.");
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            original.Body,
            original.DestinationNumber);
        resend.MarkResendOf(original.Id, idempotencyKey);
        await _notifications.AddAsync(resend, cancellationToken);

        await SubmitToProviderAsync(resend, sendAt: null, cancellationToken);
        return Result<OrderNotification>.Success(resend);
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
                var snapshot = await _smsProvider.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot != null)
                {
                    notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.Body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}: {Message}", notification.Id, ex.Message);
                return Result.Error("The provider could not dispose of the message content.");
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<NotificationReconciliationReport>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            return AppResults.Invalid<NotificationReconciliationReport>("to", "'to' must be on or after 'from'.");
        }

        var fromNumber = _smsProvider.ConfiguredFromNumber;
        if (string.IsNullOrWhiteSpace(fromNumber))
        {
            return Result<NotificationReconciliationReport>.Error("Twilio:FromNumber is not configured.");
        }

        IReadOnlyList<SmsMessageSnapshot> providerMessages;
        try
        {
            providerMessages = await _smsProvider.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Reconciliation list from provider failed: {Message}", ex.Message);
            return Result<NotificationReconciliationReport>.Error("The provider message list could not be retrieved.");
        }

        var local = await _notifications.ListAsync(
            new OrderNotificationsCreatedBetweenSpecification(from.AddDays(-1), to.AddDays(1)),
            cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var entries = new List<NotificationReconciliationEntry>();

        foreach (var pair in providerBySid)
        {
            localBySid.TryGetValue(pair.Key, out var match);
            entries.Add(new NotificationReconciliationEntry
            {
                ProviderMessageSid = pair.Key,
                NotificationId = match?.Id,
                Kind = match?.Kind.ToString(),
                ProviderStatus = pair.Value.Status,
                DateSent = pair.Value.DateSent,
                Direction = pair.Value.Direction,
                InProvider = true,
                InEshop = match != null
            });
        }

        foreach (var pair in localBySid)
        {
            if (providerBySid.ContainsKey(pair.Key))
            {
                continue;
            }

            entries.Add(new NotificationReconciliationEntry
            {
                ProviderMessageSid = pair.Key,
                NotificationId = pair.Value.Id,
                Kind = pair.Value.Kind.ToString(),
                ProviderStatus = pair.Value.ProviderStatus,
                Direction = null,
                InProvider = false,
                InEshop = true
            });
        }

        var matched = entries.Count(e => e.InProvider && e.InEshop);
        var providerOnly = entries.Count(e => e.InProvider && !e.InEshop);
        var eshopOnly = entries.Count(e => !e.InProvider && e.InEshop);

        return Result<NotificationReconciliationReport>.Success(new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = fromNumber,
            Entries = entries,
            ProviderCount = providerBySid.Count,
            EshopCount = localBySid.Count,
            MatchedCount = matched,
            ProviderOnlyCount = providerOnly,
            EshopOnlyCount = eshopOnly
        });
    }

    public async Task RefreshProviderStateAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            if (IsTerminal(notification.ProviderStatus) && notification.LastSyncedAt > DateTimeOffset.UtcNow.AddSeconds(-5)
                && notification.Kind != OrderNotificationKind.DeliveryFollowUp)
            {
                // Still refresh recently-terminal messages during verification windows via explicit fetch below.
            }

            try
            {
                var snapshot = await _smsProvider.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot == null)
                {
                    continue;
                }

                notification.ApplyProviderSnapshot(
                    snapshot.Status,
                    snapshot.ErrorCode,
                    notification.ContentRedacted ? null : snapshot.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to refresh provider state for notification {NotificationId}: {Message}",
                    notification.Id, ex.Message);
            }
        }
    }

    private async Task TryNotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destination = await ResolveDestinationAsync(order.BuyerId, cancellationToken);
            if (destination == null)
            {
                _logger.LogInformation("Skipping {Kind} SMS for order {OrderId}; shopper has no number on file.", kind, order.Id);
                return;
            }

            var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, destination);
            await _notifications.AddAsync(notification, cancellationToken);
            await SubmitToProviderAsync(notification, sendAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Notification {Kind} for order {OrderId} did not send: {Message}", kind, order.Id, ex.Message);
        }
    }

    private async Task SubmitToProviderAsync(
        OrderNotification notification,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsProvider.SendAsync(
                new SmsSendRequest
                {
                    To = notification.DestinationNumber,
                    Body = notification.Body ?? string.Empty,
                    SendAt = sendAt
                },
                cancellationToken);

            if (result.Accepted && !string.IsNullOrEmpty(result.MessageSid))
            {
                notification.RecordProviderAcceptance(result.MessageSid, result.Status ?? "queued", sendAt);
            }
            else
            {
                notification.RecordSendFailure(result.FailureReason ?? $"Provider rejected the message ({result.ErrorCode}).");
                if (result.ErrorCode.HasValue)
                {
                    notification.ApplyProviderSnapshot(result.Status ?? "failed", result.ErrorCode, null);
                }
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Provider submit failed for notification {NotificationId}: {Message}", notification.Id, ex.Message);
            notification.RecordSendFailure("The messaging provider could not accept the message.");
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task<string?> ResolveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.PhoneNumber;
    }

    private static bool IsTerminal(string? status)
    {
        return status is "delivered" or "undelivered" or "failed" or "canceled" or "failed_to_submit";
    }
}
