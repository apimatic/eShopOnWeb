using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly ITwilioSmsClient _twilio;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ShopperContactNumber> contactNumbers,
        ITwilioSmsClient twilio,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _twilio = twilio;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order) =>
        SendLifecycleMessageAsync(order, OrderNotificationKind.OrderPlaced, BuildBody(OrderNotificationKind.OrderPlaced, order.Id), sendAt: null);

    public async Task NotifyOrderDispatchedAsync(Order order)
    {
        await SendLifecycleMessageAsync(order, OrderNotificationKind.OrderDispatched, BuildBody(OrderNotificationKind.OrderDispatched, order.Id), sendAt: null);
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await SendLifecycleMessageAsync(order, OrderNotificationKind.DeliveryFollowUp, BuildBody(OrderNotificationKind.DeliveryFollowUp, order.Id), sendAt);
    }

    public async Task NotifyOrderCancelledAsync(Order order)
    {
        await CancelFollowUpsAsync(order.Id);
        await SendLifecycleMessageAsync(order, OrderNotificationKind.OrderCancelled, BuildBody(OrderNotificationKind.OrderCancelled, order.Id), sendAt: null);
    }

    public async Task CancelScheduledForDestinationAsync(string buyerId, string destinationE164)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationE164, nameof(destinationE164));

        var scheduled = await _notifications.ListAsync(
            new ScheduledNotificationsByDestinationSpecification(buyerId, destinationE164));
        await CancelNotificationsAsync(scheduled);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId)
    {
        var list = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId));
        await RefreshFromProviderAsync(list);
        return list;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var list = await _notifications.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId));
        await RefreshFromProviderAsync(list);
        return list;
    }

    public async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var latest = await _twilio.FetchAsync(notification.ProviderMessageSid);
                notification.ApplyProviderState(latest.Status, latest.ErrorCode, notification.ContentRedacted ? null : latest.Body);
                await _notifications.UpdateAsync(notification);
            }
            catch (TwilioClientException ex)
            {
                _logger.LogWarning(
                    "Could not refresh notification {NotificationId} (provider {Sid}) HTTP {Status} code {Code}.",
                    notification.Id, notification.ProviderMessageSid ?? string.Empty, ex.HttpStatus, ex.ErrorCode ?? 0);
            }
        }
    }

    public async Task<Result<OrderNotification>> ResendAsync(int notificationId, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByResendKeySpecification(notificationId, idempotencyKey));
        if (existing is not null)
        {
            return Result<OrderNotification>.Success(existing);
        }

        var original = await _notifications.GetByIdAsync(notificationId);
        if (original is null)
        {
            return Result<OrderNotification>.NotFound();
        }

        if (!string.IsNullOrEmpty(original.ProviderMessageSid))
        {
            try
            {
                var latest = await _twilio.FetchAsync(original.ProviderMessageSid);
                original.ApplyProviderState(latest.Status, latest.ErrorCode, original.ContentRedacted ? null : latest.Body);
                await _notifications.UpdateAsync(original);
            }
            catch (TwilioClientException ex)
            {
                _logger.LogWarning(
                    "Could not refresh notification {NotificationId} before resend HTTP {Status} code {Code}.",
                    original.Id, ex.HttpStatus, ex.ErrorCode ?? 0);
            }
        }

        if (!original.DidNotReachShopper())
        {
            return Result<OrderNotification>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "notificationId", ErrorMessage = "Only messages that did not reach the shopper can be re-sent." }
            });
        }

        var destinationStillOnFile = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndE164Specification(original.BuyerId, original.DestinationE164));
        if (destinationStillOnFile is null)
        {
            return Result<OrderNotification>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "notificationId", ErrorMessage = "The destination is no longer on file for this shopper." }
            });
        }

        var body = original.ContentRedacted || string.IsNullOrEmpty(original.Body)
            ? BuildBody(original.Kind == OrderNotificationKind.Resend ? OrderNotificationKind.OrderPlaced : original.Kind, original.OrderId)
            : original.Body;

        var resent = await DeliverAsync(
            original.OrderId,
            original.BuyerId,
            OrderNotificationKind.Resend,
            original.DestinationE164,
            body!,
            sendAt: null,
            sourceNotificationId: original.Id,
            idempotencyKey: idempotencyKey);

        return Result<OrderNotification>.Success(resent);
    }

    public async Task<Result> RedactContentAsync(int notificationId)
    {
        var notification = await _notifications.GetByIdAsync(notificationId);
        if (notification is null)
        {
            return Result.NotFound();
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var updated = await _twilio.UpdateAsync(notification.ProviderMessageSid, body: string.Empty, status: null);
                notification.ApplyProviderState(updated.Status, updated.ErrorCode, bodyIfNotRedacted: null);
            }
            catch (TwilioClientException ex)
            {
                _logger.LogWarning(
                    "Provider content disposal failed for notification {NotificationId} HTTP {Status} code {Code}.",
                    notification.Id, ex.HttpStatus, ex.ErrorCode ?? 0);
                return Result.Error($"The provider could not dispose of the message content (HTTP {ex.HttpStatus}, code {ex.ErrorCode}).");
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification);
        return Result.Success();
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var fromNumber = _twilio.FromNumber;
        var providerMessages = await _twilio.ListFromSenderAsync(fromNumber, from, to);
        var applicationRecords = await _notifications.ListAsync(
            new OrderNotificationsWithProviderSidSpecification(from, to));

        var applicationBySid = applicationRecords
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciledNotification>();
        var applicationOnly = new List<ReconciledNotification>();
        var providerOnly = new List<ProviderOnlyMessage>();

        foreach (var pair in applicationBySid)
        {
            var dto = ToReconciled(pair.Value);
            if (providerBySid.ContainsKey(pair.Key))
            {
                matched.Add(dto);
            }
            else
            {
                applicationOnly.Add(dto);
            }
        }

        foreach (var pair in providerBySid)
        {
            if (!applicationBySid.ContainsKey(pair.Key))
            {
                providerOnly.Add(new ProviderOnlyMessage
                {
                    ProviderMessageSid = pair.Value.Sid!,
                    Status = pair.Value.Status,
                    DateSent = pair.Value.DateSent,
                    DateCreated = pair.Value.DateCreated
                });
            }
        }

        var unresolvedApplicationOnly = new List<ReconciledNotification>();
        foreach (var item in applicationOnly)
        {
            if (string.IsNullOrEmpty(item.ProviderMessageSid))
            {
                unresolvedApplicationOnly.Add(item);
                continue;
            }

            try
            {
                await _twilio.FetchAsync(item.ProviderMessageSid);
                matched.Add(item);
            }
            catch (TwilioClientException)
            {
                unresolvedApplicationOnly.Add(item);
            }
        }

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = fromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            ApplicationOnly = unresolvedApplicationOnly
        };
    }

    private async Task SendLifecycleMessageAsync(Order order, OrderNotificationKind kind, string body, DateTimeOffset? sendAt)
    {
        var destination = await ResolveDestinationAsync(order.BuyerId);
        if (destination is null)
        {
            _logger.LogInformation("Skipping {Kind} SMS for order {OrderId}; shopper has no number on file.", kind, order.Id);
            return;
        }

        await DeliverAsync(order.Id, order.BuyerId, kind, destination, body, sendAt, sourceNotificationId: null, idempotencyKey: null);
    }

    private async Task<string?> ResolveDestinationAsync(string buyerId)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        return numbers.OrderByDescending(n => n.Id).FirstOrDefault()?.E164Number;
    }

    private async Task<OrderNotification> DeliverAsync(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string destinationE164,
        string body,
        DateTimeOffset? sendAt,
        int? sourceNotificationId,
        string? idempotencyKey)
    {
        string status;
        string? sid = null;
        int? errorCode = null;

        try
        {
            var sent = await _twilio.SendAsync(destinationE164, body, sendAt);
            status = sent.Status;
            sid = sent.Sid;
            errorCode = sent.ErrorCode;
        }
        catch (TwilioClientException ex)
        {
            _logger.LogWarning(
                "SMS send failed for order {OrderId} kind {Kind} HTTP {Status} code {Code}. The order operation will continue.",
                orderId, kind, ex.HttpStatus, ex.ErrorCode ?? 0);
            status = "failed";
            errorCode = ex.ErrorCode;
        }
        catch (Exception ex)
        {
            _logger.LogError("SMS send failed for order {OrderId} kind {Kind}: {ExceptionType}. The order operation will continue.", orderId, kind, ex.GetType().Name);
            status = "failed";
        }

        var notification = new OrderNotification(
            orderId,
            buyerId,
            kind,
            destinationE164,
            body,
            sid,
            status,
            errorCode,
            sendAt,
            sourceNotificationId,
            idempotencyKey);

        await _notifications.AddAsync(notification);
        return notification;
    }

    private async Task CancelFollowUpsAsync(int orderId)
    {
        var followUps = await _notifications.ListAsync(new FollowUpNotificationsByOrderSpecification(orderId));
        await CancelNotificationsAsync(followUps);
    }

    private async Task CancelNotificationsAsync(IEnumerable<OrderNotification> notifications)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var latest = await _twilio.FetchAsync(notification.ProviderMessageSid);
                notification.ApplyProviderState(latest.Status, latest.ErrorCode, notification.ContentRedacted ? null : latest.Body);
                if (!notification.IsScheduledPending())
                {
                    await _notifications.UpdateAsync(notification);
                    continue;
                }

                var updated = await _twilio.UpdateAsync(notification.ProviderMessageSid, body: null, status: "canceled");
                notification.ApplyProviderState(updated.Status, updated.ErrorCode, notification.ContentRedacted ? null : updated.Body);
                await _notifications.UpdateAsync(notification);
            }
            catch (TwilioClientException ex)
            {
                _logger.LogWarning(
                    "Failed to cancel notification {NotificationId} (provider {Sid}) HTTP {Status} code {Code}.",
                    notification.Id, notification.ProviderMessageSid ?? string.Empty, ex.HttpStatus, ex.ErrorCode ?? 0);
            }
        }
    }

    private static string BuildBody(OrderNotificationKind kind, int orderId) => kind switch
    {
        OrderNotificationKind.OrderPlaced => $"Your eShop order #{orderId} has been placed. Thank you.",
        OrderNotificationKind.OrderDispatched => $"Your eShop order #{orderId} is on its way.",
        OrderNotificationKind.DeliveryFollowUp => $"How did the delivery of eShop order #{orderId} go?",
        OrderNotificationKind.OrderCancelled => $"Your eShop order #{orderId} has been cancelled.",
        _ => $"An update on eShop order #{orderId}."
    };

    private static ReconciledNotification ToReconciled(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        ProviderMessageSid = notification.ProviderMessageSid,
        Kind = notification.Kind.ToString(),
        Status = notification.ProviderStatus
    };
}
