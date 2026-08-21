using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationSender
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsGateway _smsGateway;

    public OrderNotificationSender(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsGateway smsGateway)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsGateway = smsGateway;
    }

    public async Task<string?> GetDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers.Count == 0 ? null : numbers[0].CanonicalNumber;
    }

    public async Task NotifyPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        await SendImmediateAsync(
            orderId,
            buyerId,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{orderId} has been placed.",
            cancellationToken);
    }

    public async Task NotifyDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        await SendImmediateAsync(
            orderId,
            buyerId,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{orderId} is on its way.",
            cancellationToken);

        await ScheduleFollowUpAsync(orderId, buyerId, cancellationToken);
    }

    public async Task NotifyCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        await CancelPendingFollowUpsAsync(orderId, cancellationToken);

        await SendImmediateAsync(
            orderId,
            buyerId,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{orderId} has been cancelled.",
            cancellationToken);
    }

    public async Task<OrderNotification?> SendImmediateAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        CancellationToken cancellationToken,
        int? resentFromNotificationId = null,
        string? destinationOverride = null)
    {
        var destination = destinationOverride ?? await GetDestinationAsync(buyerId, cancellationToken);
        if (string.IsNullOrWhiteSpace(destination))
        {
            return null;
        }

        var notification = new OrderNotification(orderId, buyerId, kind, body, destination);
        if (resentFromNotificationId is int sourceId)
        {
            notification.MarkResentFrom(sourceId);
        }

        notification = await _notifications.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _smsGateway.SendImmediateAsync(destination, body, cancellationToken);
            notification.RecordProviderAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, sendAt: null);
        }
        catch (Exception ex) when (IsSendFailure(ex))
        {
            notification.RecordLocalFailure(DescribeSendFailure(ex));
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    private async Task ScheduleFollowUpAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var destination = await GetDestinationAsync(buyerId, cancellationToken);
        if (string.IsNullOrWhiteSpace(destination))
        {
            return;
        }

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = $"How did delivery go for your eShopOnWeb order #{orderId}?";
        var notification = new OrderNotification(orderId, buyerId, NotificationKind.DeliveryFollowUp, body, destination);
        notification = await _notifications.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _smsGateway.ScheduleAsync(destination, body, sendAt, cancellationToken);
            notification.RecordProviderAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, sendAt);
        }
        catch (Exception ex) when (IsSendFailure(ex))
        {
            notification.RecordLocalFailure(DescribeSendFailure(ex));
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new FollowUpNotificationsByOrderSpec(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrWhiteSpace(followUp.ProviderMessageSid))
            {
                continue;
            }

            if (IsTerminalOutboundStatus(followUp.ProviderStatus))
            {
                continue;
            }

            try
            {
                await _smsGateway.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                var latest = await _smsGateway.FetchAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.ApplyProviderState(latest.Status, latest.ErrorCode, latest.ErrorMessage);
            }
            catch (Exception ex) when (IsSendFailure(ex))
            {
                // Cancel is best-effort: the order cancel itself must still succeed.
            }

            await _notifications.UpdateAsync(followUp, cancellationToken);
        }
    }

    public async Task RefreshFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var latest = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(latest.Status, latest.ErrorCode, latest.ErrorMessage);
            if (notification.ContentDisposed || string.IsNullOrEmpty(latest.Body))
            {
                if (string.IsNullOrEmpty(latest.Body))
                {
                    notification.DisposeContent();
                }
            }
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (IsSendFailure(ex))
        {
            // Keep the last stored provider state when refresh fails.
        }
    }

    public static bool IsSendFailure(Exception ex) =>
        ex is SmsGatewayException or System.Text.Json.JsonException or System.Net.Http.HttpRequestException or TaskCanceledException or OperationCanceledException;

    public static string DescribeSendFailure(Exception ex) => ex switch
    {
        SmsGatewayException gateway when gateway.StatusCode is int status => $"provider_{status}",
        System.Text.Json.JsonException => "unreadable_provider_response",
        System.Net.Http.HttpRequestException => "provider_unreachable",
        TaskCanceledException or OperationCanceledException => "provider_timeout",
        _ => "send_failed"
    };

    public static bool ReachedShopper(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return status is "delivered" or "sent" or "read" or "partially_delivered";
    }

    private static bool IsTerminalOutboundStatus(string? status) =>
        status is "delivered" or "sent" or "read" or "failed" or "undelivered" or "canceled" or "not_sent";
}
