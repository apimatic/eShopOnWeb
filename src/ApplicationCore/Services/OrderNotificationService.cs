using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    internal static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<NotificationResendIdempotency> _resendKeys;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<NotificationResendIdempotency> resendKeys,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _resendKeys = resendKeys;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken) =>
        SendToShopperNumbersAsync(
            orderId,
            buyerId,
            OrderNotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{orderId} has been placed.",
            sendAt: null,
            cancellationToken);

    public async Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        await SendToShopperNumbersAsync(
            orderId,
            buyerId,
            OrderNotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{orderId} is on its way.",
            sendAt: null,
            cancellationToken);

        await SendToShopperNumbersAsync(
            orderId,
            buyerId,
            OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShopOnWeb order #{orderId} go?",
            sendAt: DateTimeOffset.UtcNow.Add(FollowUpDelay),
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        await CancelPendingFollowUpsAsync(orderId, cancellationToken);

        await SendToShopperNumbersAsync(
            orderId,
            buyerId,
            OrderNotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{orderId} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        var list = await _notifications.ListAsync(
            new NotificationsByOrderIdSpecification(orderId),
            cancellationToken);
        await RefreshFromProviderAsync(list, cancellationToken);
        return list;
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<OrderNotification>>> ListForOrdersAsync(
        IReadOnlyList<int> orderIds,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<OrderNotification>>();
        }

        var list = await _notifications.ListAsync(
            new NotificationsByOrderIdsSpecification(orderIds),
            cancellationToken);
        await RefreshFromProviderAsync(list, cancellationToken);

        return list
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
    }

    public async Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new NotificationResendNotAllowedException("An idempotency key is required.");
        }

        var existingKey = await _resendKeys.FirstOrDefaultAsync(
            new ResendIdempotencySpecification(notificationId, idempotencyKey.Trim()),
            cancellationToken);
        if (existingKey is not null)
        {
            var previous = await _notifications.GetByIdAsync(existingKey.ResultNotificationId, cancellationToken);
            if (previous is not null)
            {
                return previous;
            }
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            throw new NotificationNotFoundException();
        }

        if (!source.DidNotReachShopper())
        {
            throw new NotificationResendNotAllowedException(
                "Only messages that did not reach the shopper can be re-sent.");
        }

        var destination = await ResolveLiveDestinationAsync(source, cancellationToken);
        if (destination is null)
        {
            throw new NotificationResendNotAllowedException(
                "The destination number is no longer on file for this shopper.");
        }

        var resend = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            source.Kind,
            destination.Id,
            destination.CanonicalNumber,
            source.Body ?? $"Update on your eShopOnWeb order #{source.OrderId}.",
            scheduledFor: null,
            parentNotificationId: source.Id);

        await SendAndPersistAsync(resend, cancellationToken);

        var record = new NotificationResendIdempotency(source.Id, idempotencyKey.Trim(), resend.Id);
        await _resendKeys.AddAsync(record, cancellationToken);

        return resend;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new NotificationNotFoundException();
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            try
            {
                var snapshot = await _smsGateway.RedactBodyAsync(notification.ProviderSid, cancellationToken);
                ApplySnapshot(notification, snapshot);
            }
            catch (Exception ex) when (ex is SmsGatewayException or System.Text.Json.JsonException)
            {
                _logger.LogWarning(
                    "Failed to dispose provider content for notification {NotificationId} (sid present). {Error}",
                    notificationId,
                    SafeError(ex));
                throw;
            }
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SmsMessageSnapshot> providerMessages;
        try
        {
            providerMessages = await _smsGateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        }
        catch (Exception ex) when (ex is SmsGatewayException or System.Text.Json.JsonException)
        {
            throw new SmsGatewayException("Unable to load the provider message list for reconciliation.", innerException: ex);
        }

        var local = await _notifications.ListAsync(
            new NotificationsWithProviderSidSpecification(),
            cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerSids = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();

        foreach (var provider in providerMessages)
        {
            if (string.IsNullOrWhiteSpace(provider.Sid))
            {
                continue;
            }

            providerSids.Add(provider.Sid);
            localBySid.TryGetValue(provider.Sid, out var match);
            entries.Add(ToEntry(provider, match, inProvider: true, inApplication: match is not null));
        }

        foreach (var localNote in local)
        {
            if (string.IsNullOrWhiteSpace(localNote.ProviderSid) || providerSids.Contains(localNote.ProviderSid))
            {
                continue;
            }

            entries.Add(new ReconciliationEntry(
                localNote.ProviderSid,
                localNote.Status,
                From: null,
                To: null,
                localNote.ContentDisposed ? null : localNote.Body,
                DateSent: null,
                DateCreated: localNote.CreatedAt.ToString("O"),
                localNote.Id,
                InProvider: false,
                InApplication: true));
        }

        return new ReconciliationReport(from, to, _smsGateway.ConfiguredFromNumber, entries, Complete: true);
    }

    private async Task SendToShopperNumbersAsync(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var numbers = await _contactNumbers.ListAsync(
                new ContactNumbersByBuyerSpecification(buyerId),
                cancellationToken);

            if (numbers.Count == 0)
            {
                return;
            }

            foreach (var number in numbers)
            {
                var notification = new OrderNotification(
                    orderId,
                    buyerId,
                    kind,
                    number.Id,
                    number.CanonicalNumber,
                    body,
                    sendAt);

                await SendAndPersistAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "Order {OrderId} notification {Kind} failed; the order operation still succeeded. {Error}",
                orderId,
                kind,
                SafeError(ex));
        }
    }

    private async Task SendAndPersistAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _notifications.AddAsync(notification, cancellationToken);

            if (string.IsNullOrWhiteSpace(notification.DestinationE164))
            {
                notification.MarkLocalSendFailed("No destination on file.");
                await _notifications.UpdateAsync(notification, cancellationToken);
                return;
            }

            SmsMessageSnapshot snapshot;
            try
            {
                snapshot = await _smsGateway.SendAsync(
                    new SmsSendRequest(notification.DestinationE164, notification.Body ?? string.Empty, notification.ScheduledFor),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is SmsGatewayException or System.Text.Json.JsonException or TaskCanceledException)
            {
                notification.MarkLocalSendFailed("The messaging provider did not accept the message.");
                await _notifications.UpdateAsync(notification, cancellationToken);
                _logger.LogWarning(
                    "Provider send failed for notification {NotificationId} kind {Kind}. {Error}",
                    notification.Id,
                    notification.Kind,
                    SafeError(ex));
                return;
            }

            ApplySnapshot(notification, snapshot);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "Failed to persist or send notification kind {Kind} for order {OrderId}. {Error}",
                notification.Kind,
                notification.OrderId,
                SafeError(ex));
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var followUps = await _notifications.ListAsync(
                new ScheduledFollowUpsByOrderSpecification(orderId),
                cancellationToken);

            foreach (var followUp in followUps)
            {
                if (string.IsNullOrWhiteSpace(followUp.ProviderSid))
                {
                    continue;
                }

                try
                {
                    var snapshot = await _smsGateway.CancelScheduledAsync(followUp.ProviderSid, cancellationToken);
                    ApplySnapshot(followUp, snapshot);
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                }
                catch (Exception ex) when (ex is SmsGatewayException or System.Text.Json.JsonException or TaskCanceledException)
                {
                    _logger.LogWarning(
                        "Failed to cancel scheduled follow-up notification {NotificationId}. {Error}",
                        followUp.Id,
                        SafeError(ex));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "Failed to cancel follow-ups for order {OrderId}. {Error}",
                orderId,
                SafeError(ex));
        }
    }

    private async Task RefreshFromProviderAsync(
        IReadOnlyList<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
                ApplySnapshot(notification, snapshot);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex) when (ex is SmsGatewayException or System.Text.Json.JsonException or TaskCanceledException)
            {
                _logger.LogWarning(
                    "Failed to refresh provider status for notification {NotificationId}. {Error}",
                    notification.Id,
                    SafeError(ex));
            }
        }
    }

    private async Task<ShopperContactNumber?> ResolveLiveDestinationAsync(
        OrderNotification source,
        CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(
            new ContactNumbersByBuyerSpecification(source.BuyerId),
            cancellationToken);

        if (source.ContactNumberId is int contactId)
        {
            var match = numbers.FirstOrDefault(n => n.Id == contactId);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static void ApplySnapshot(OrderNotification notification, SmsMessageSnapshot snapshot)
    {
        notification.ApplyProviderResult(
            snapshot.Sid,
            snapshot.Status,
            snapshot.Body,
            snapshot.ErrorCode,
            snapshot.ErrorMessage);
    }

    private static ReconciliationEntry ToEntry(
        SmsMessageSnapshot provider,
        OrderNotification? local,
        bool inProvider,
        bool inApplication)
    {
        return new ReconciliationEntry(
            provider.Sid,
            provider.Status ?? local?.Status,
            provider.From,
            provider.To,
            local is { ContentDisposed: true } ? null : provider.Body,
            provider.DateSent,
            provider.DateCreated,
            local?.Id,
            inProvider,
            inApplication);
    }

    private static string SafeError(Exception ex)
    {
        return ex switch
        {
            SmsGatewayException sms => $"SmsGatewayException status={sms.HttpStatusCode}",
            System.Text.Json.JsonException => "JsonException",
            TaskCanceledException => "TaskCanceledException",
            _ => ex.GetType().Name
        };
    }
}
