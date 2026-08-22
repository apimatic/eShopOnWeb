using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendKey> _resendKeys;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendKey> resendKeys,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _resendKeys = resendKeys;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
        => SendToBuyerNumbersAsync(order, NotificationKind.OrderPlaced, BuildBody(NotificationKind.OrderPlaced, order.Id), sendAt: null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendToBuyerNumbersAsync(order, NotificationKind.OrderDispatched, BuildBody(NotificationKind.OrderDispatched, order.Id), sendAt: null, cancellationToken);
        var followUpAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await SendToBuyerNumbersAsync(order, NotificationKind.DeliveryFollowUp, BuildBody(NotificationKind.DeliveryFollowUp, order.Id), followUpAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await SendToBuyerNumbersAsync(order, NotificationKind.OrderCancelled, BuildBody(NotificationKind.OrderCancelled, order.Id), sendAt: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        var list = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        if (refreshFromProvider)
        {
            await RefreshFromProviderAsync(list, cancellationToken);
        }

        return list;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var list = await _notifications.ListAsync(new NotificationsByBuyerSpecification(buyerId), cancellationToken);
        if (refreshFromProvider)
        {
            await RefreshFromProviderAsync(list, cancellationToken);
        }

        return list;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existingKey = await _resendKeys.FirstOrDefaultAsync(
            new NotificationResendKeySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existingKey is not null)
        {
            var previous = await _notifications.GetByIdAsync(existingKey.ResultNotificationId, cancellationToken);
            if (previous is not null)
            {
                return previous;
            }
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        var stillRegistered = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByCanonicalSpecification(original.BuyerId, original.DestinationPhoneNumber),
            cancellationToken);
        if (stillRegistered is null)
        {
            throw new InvalidContactNumberException("The destination is no longer registered and cannot be messaged.");
        }

        var body = original.ContentRedacted || string.IsNullOrEmpty(original.Body)
            ? BuildBody(original.Kind, original.OrderId)
            : original.Body;

        var resent = await SendOneAsync(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            original.DestinationPhoneNumber,
            body,
            sendAt: null,
            original.Id,
            cancellationToken);

        var key = new NotificationResendKey(original.Id, idempotencyKey, resent.Id);
        await _resendKeys.AddAsync(key, cancellationToken);
        return resent;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}: {Error}", notificationId, ex.Message);
                throw;
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' instant must not be earlier than 'from'.");
        }

        var providerMessages = await _smsGateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localInRange = await _notifications.ListAsync(new NotificationsCreatedInRangeSpecification(from, to), cancellationToken);
        var localBySid = localInRange
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        if (providerBySid.Count > 0)
        {
            var matchingLocals = await _notifications.ListAsync(
                new NotificationsByProviderSidsSpecification(providerBySid.Keys.ToArray()),
                cancellationToken);
            foreach (var local in matchingLocals)
            {
                if (!string.IsNullOrEmpty(local.ProviderMessageSid))
                {
                    localBySid[local.ProviderMessageSid] = local;
                }
            }
        }

        var matches = new List<ReconciledNotification>();
        var providerOnly = new List<ProviderOnlyMessage>();
        var eShopOnly = new List<ReconciledNotification>();

        foreach (var provider in providerBySid)
        {
            if (localBySid.TryGetValue(provider.Key, out var local))
            {
                matches.Add(ToReconciled(local));
            }
            else
            {
                providerOnly.Add(new ProviderOnlyMessage(
                    provider.Value.Sid,
                    provider.Value.Status,
                    provider.Value.DateCreated,
                    provider.Value.DateSent));
            }
        }

        foreach (var local in localBySid)
        {
            if (!providerBySid.ContainsKey(local.Key))
            {
                eShopOnly.Add(ToReconciled(local.Value));
            }
        }

        return new NotificationReconciliationReport(from, to, _smsGateway.FromNumber, matches, providerOnly, eShopOnly);
    }

    private static ReconciledNotification ToReconciled(OrderNotification notification)
        => new(notification.Id, notification.ProviderMessageSid, notification.Kind.ToString(), notification.ProviderStatus, notification.CreatedAt);

    private async Task SendToBuyerNumbersAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
            if (numbers.Count == 0)
            {
                _logger.LogInformation("Skipping {Kind} SMS for order {OrderId}; shopper has no number on file.", kind, order.Id);
                return;
            }

            foreach (var number in numbers)
            {
                await SendOneAsync(order.Id, order.BuyerId, kind, number.CanonicalPhoneNumber, body, sendAt, resentFrom: null, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId} {Kind} notification failed without failing the order: {Error}", order.Id, kind, ex.Message);
        }
    }

    private async Task<OrderNotification> SendOneAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string destination,
        string body,
        DateTimeOffset? sendAt,
        int? resentFrom,
        CancellationToken cancellationToken)
    {
        SmsMessageSnapshot? snapshot = null;
        string? sendError = null;
        try
        {
            snapshot = await _smsGateway.SendAsync(new SmsSendRequest(destination, body, sendAt), cancellationToken);
        }
        catch (Exception ex)
        {
            sendError = "Provider send failed";
            _logger.LogWarning("Provider send failed for order {OrderId} kind {Kind}: {Error}", orderId, kind, ex.Message);
        }

        var notification = new OrderNotification(
            orderId,
            buyerId,
            kind,
            destination,
            body,
            snapshot?.Sid,
            snapshot?.Status,
            snapshot?.ErrorCode,
            snapshot?.ErrorMessage ?? sendError,
            sendAt,
            resentFrom);

        if (snapshot is null && sendError is not null)
        {
            notification.MarkSendFailed(sendError);
        }

        return await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (!followUp.IsCancellableFollowUp() || string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var updated = await _smsGateway.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.ApplyProviderOutcome(updated.Status, updated.ErrorCode, updated.ErrorMessage, updated.Body);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel follow-up for order {OrderId} notification {NotificationId}: {Error}", orderId, followUp.Id, ex.Message);
            }
        }
    }

    private async Task RefreshFromProviderAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot is null)
                {
                    continue;
                }

                notification.ApplyProviderOutcome(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh notification {NotificationId} from provider: {Error}", notification.Id, ex.Message);
            }
        }
    }

    public static string BuildBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShopOnWeb: Your order #{orderId} has been placed. We will update you as it progresses.",
        NotificationKind.OrderDispatched => $"eShopOnWeb: Order #{orderId} is on its way.",
        NotificationKind.DeliveryFollowUp => $"eShopOnWeb: How did delivery of order #{orderId} go? Reply with your feedback.",
        NotificationKind.OrderCancelled => $"eShopOnWeb: Order #{orderId} has been cancelled.",
        _ => $"eShopOnWeb: An update for order #{orderId}."
    };
}
