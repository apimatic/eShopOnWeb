using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendRecord> _resendRecords;
    private readonly IContactNumberService _contactNumbers;
    private readonly ISmsNotificationClient _sms;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendRecord> resendRecords,
        IContactNumberService contactNumbers,
        ISmsNotificationClient sms,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _notifications = notifications;
        _resendRecords = resendRecords;
        _contactNumbers = contactNumbers;
        _sms = sms;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
    {
        Guard.Against.Null(order, nameof(order));
        return TrySendAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed. Thank you.",
            sendAt: null,
            sourceNotificationId: null,
            cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        Guard.Against.Null(order, nameof(order));
        await TrySendAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} has been dispatched and is on its way.",
            sendAt: null,
            sourceNotificationId: null,
            cancellationToken);

        await TrySendAsync(
            order,
            OrderNotificationKind.DispatchFollowUp,
            $"How did the delivery of your eShopOnWeb order #{order.Id} go? Reply with your feedback.",
            sendAt: DateTimeOffset.UtcNow.Add(FollowUpDelay),
            sourceNotificationId: null,
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        Guard.Against.Null(order, nameof(order));
        await CancelOutstandingFollowUpsAsync(order.Id, cancellationToken);
        await TrySendAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            sendAt: null,
            sourceNotificationId: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpec(orderId), cancellationToken);
        if (order is null || (!isAdministrator && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal)))
        {
            throw new EntityNotFoundException("Order was not found.");
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpec(orderId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var notifications = await _notifications.ListAsync(new NotificationsByBuyerSpec(buyerId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existingReplay = await _resendRecords.FirstOrDefaultAsync(
            new NotificationResendByKeySpec(notificationId, idempotencyKey), cancellationToken);
        if (existingReplay is not null)
        {
            var replayed = await _notifications.GetByIdAsync(existingReplay.ResultNotificationId, cancellationToken);
            if (replayed is not null)
            {
                return replayed;
            }
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                       ?? throw new EntityNotFoundException("Notification was not found.");

        if (original.Kind == OrderNotificationKind.DispatchFollowUp &&
            string.Equals(original.Status, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOrderStateException("A cancelled delivery follow-up cannot be re-sent.");
        }

        var order = await _orders.GetByIdAsync(original.OrderId, cancellationToken)
                    ?? throw new EntityNotFoundException("Order was not found.");

        var number = await _contactNumbers.GetPrimaryAsync(original.BuyerId, cancellationToken);
        if (number is null)
        {
            throw new ContactNumberRejectedException("The shopper has no contact number on file.");
        }

        var body = string.IsNullOrWhiteSpace(original.Body)
            ? $"An update about your eShopOnWeb order #{order.Id}."
            : original.Body;

        var resent = new OrderNotification(
            order.Id,
            original.BuyerId,
            OrderNotificationKind.Resend,
            body,
            scheduledSendAt: null,
            sourceNotificationId: original.Id);
        await _notifications.AddAsync(resent, cancellationToken);

        await DispatchToProviderAsync(resent, number.CanonicalNumber, body, sendAt: null, cancellationToken);

        await _resendRecords.AddAsync(new NotificationResendRecord(notificationId, idempotencyKey, resent.Id), cancellationToken);
        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                           ?? throw new EntityNotFoundException("Notification was not found.");

        if (!string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            try
            {
                var result = await _sms.RedactBodyAsync(notification.ProviderSid, cancellationToken);
                notification.ApplyProviderState(
                    result.Sid,
                    result.Status,
                    result.ErrorCode,
                    result.ErrorMessage,
                    result.DateCreated,
                    result.DateSent,
                    result.DateUpdated,
                    body: null);
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning(
                    "Provider content disposal failed for notification {NotificationId} with HTTP {Status}.",
                    notification.Id,
                    ex.StatusCode?.ToString() ?? "none");
                throw;
            }
        }

        notification.MarkContentRedacted(null);
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new ContactNumberRejectedException("The 'to' timestamp must not be earlier than 'from'.");
        }

        var providerPage = await _sms.ListSentFromAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsInDateRangeSpec(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerSids = new HashSet<string>(StringComparer.Ordinal);
        var matched = new List<MatchedNotification>();
        var providerOnly = new List<SmsMessageResult>();

        foreach (var message in providerPage.Messages)
        {
            if (string.IsNullOrWhiteSpace(message.Sid))
            {
                providerOnly.Add(message);
                continue;
            }

            providerSids.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var localRow))
            {
                matched.Add(new MatchedNotification
                {
                    NotificationId = localRow.Id,
                    OrderId = localRow.OrderId,
                    ProviderSid = message.Sid,
                    ApplicationStatus = localRow.Status,
                    ProviderStatus = message.Status
                });
            }
            else
            {
                providerOnly.Add(message);
            }
        }

        var applicationOnly = local
            .Where(n => string.IsNullOrWhiteSpace(n.ProviderSid) || !providerSids.Contains(n.ProviderSid))
            .Select(n => new ApplicationOnlyNotification
            {
                NotificationId = n.Id,
                OrderId = n.OrderId,
                ProviderSid = n.ProviderSid,
                Status = n.Status,
                Kind = n.Kind.ToString()
            })
            .ToList();

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = providerPage.FromNumber,
            Truncated = providerPage.Truncated,
            Matched = matched,
            ProviderOnly = providerOnly,
            ApplicationOnly = applicationOnly
        };
    }

    private async Task CancelOutstandingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new OutstandingFollowUpNotificationsSpec(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (!followUp.IsScheduledFollowUpOutstanding())
            {
                continue;
            }

            try
            {
                var result = await _sms.CancelScheduledAsync(followUp.ProviderSid!, cancellationToken);
                followUp.ApplyProviderState(
                    result.Sid,
                    result.Status,
                    result.ErrorCode,
                    result.ErrorMessage,
                    result.DateCreated,
                    result.DateSent,
                    result.DateUpdated,
                    result.Body);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning(
                    "Could not cancel scheduled follow-up {NotificationId} for order {OrderId}. HTTP {Status}.",
                    followUp.Id,
                    orderId,
                    ex.StatusCode?.ToString() ?? "none");
                await TryRefreshOneAsync(followUp, cancellationToken);
            }
        }
    }

    private async Task TrySendAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        int? sourceNotificationId,
        CancellationToken cancellationToken)
    {
        var number = await _contactNumbers.GetPrimaryAsync(order.BuyerId, cancellationToken);
        if (number is null)
        {
            _logger.LogInformation("No contact number on file for order {OrderId}; skipping SMS.", order.Id);
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, sendAt, sourceNotificationId);
        await _notifications.AddAsync(notification, cancellationToken);
        await DispatchToProviderAsync(notification, number.CanonicalNumber, body, sendAt, cancellationToken);
    }

    private async Task DispatchToProviderAsync(
        OrderNotification notification,
        string destination,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = sendAt is null
                ? await _sms.SendAsync(destination, body, cancellationToken)
                : await _sms.ScheduleAsync(destination, body, sendAt.Value, cancellationToken);

            notification.ApplyProviderState(
                result.Sid,
                result.Status,
                result.ErrorCode,
                result.ErrorMessage,
                result.DateCreated,
                result.DateSent,
                result.DateUpdated,
                result.Body);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (IsNotificationTransportFailure(ex))
        {
            _logger.LogWarning(
                "SMS {Kind} for order {OrderId} did not complete. {Reason}",
                notification.Kind,
                notification.OrderId,
                ex.GetType().Name);
            notification.MarkLocalFailure("The messaging provider did not accept the message.");
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            await TryRefreshOneAsync(notification, cancellationToken);
        }
    }

    private async Task TryRefreshOneAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderSid)
            || notification.ContentRedacted
            || OrderNotification.IsTerminalStatus(notification.Status))
        {
            return;
        }

        try
        {
            var result = await _sms.FetchAsync(notification.ProviderSid, cancellationToken);
            notification.ApplyProviderState(
                result.Sid,
                result.Status,
                result.ErrorCode,
                result.ErrorMessage,
                result.DateCreated,
                result.DateSent,
                result.DateUpdated,
                notification.ContentRedacted ? null : result.Body);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (IsNotificationTransportFailure(ex))
        {
            _logger.LogWarning(
                "Could not refresh notification {NotificationId} from the provider. {Reason}",
                notification.Id,
                ex.GetType().Name);
        }
    }

    private static bool IsNotificationTransportFailure(Exception ex) =>
        ex is SmsProviderException
            or HttpRequestException
            or TaskCanceledException
            or JsonException
            || (ex is SmsProviderException provider && provider.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
}
