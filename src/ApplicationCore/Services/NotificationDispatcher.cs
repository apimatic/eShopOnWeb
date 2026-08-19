using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Turns order events into SMS messages and keeps local notification records in step with
/// the provider. Every public method is best-effort and self-contained: a failure to send,
/// schedule or cancel is recorded and logged (without PII) but never propagates, so the
/// order operation that triggered it always succeeds.
/// </summary>
public class NotificationDispatcher : INotificationDispatcher
{
    /// <summary>How far ahead the "how did delivery go?" follow-up is scheduled.</summary>
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Notification> _notifications;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messaging;
    private readonly ISmsConfiguration _configuration;
    private readonly IAppLogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        IRepository<Notification> notifications,
        IReadRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient messaging,
        ISmsConfiguration configuration,
        IAppLogger<NotificationDispatcher> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendOrderEventAsync(Order order, NotificationKind kind, CancellationToken cancellationToken = default)
    {
        try
        {
            var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
            if (numbers.Count == 0)
            {
                _logger.LogInformation("Order {OrderId}: no number on file, so no {Kind} message was sent.", order.Id, kind);
                return;
            }

            var body = BuildBody(order, kind);
            foreach (var contactNumber in numbers)
            {
                var notification = new Notification(order.Id, order.BuyerId, kind, contactNumber.PhoneNumber, body);
                try
                {
                    var message = await _messaging.SendMessageAsync(new SendMessageCommand
                    {
                        To = contactNumber.PhoneNumber,
                        Body = body,
                        From = _configuration.SenderNumber
                    }, cancellationToken);
                    notification.RecordProviderResult(message.Sid, message.Status, message.ErrorCode, message.ErrorMessage);
                }
                catch (Exception ex)
                {
                    notification.RecordSendFailure(Summarize(ex));
                    _logger.LogWarning("Order {OrderId}: a {Kind} message could not be sent ({Reason}).", order.Id, kind, Summarize(ex));
                }

                await _notifications.AddAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Never let notification work fail the underlying order operation.
            _logger.LogWarning("Order {OrderId}: raising {Kind} notifications failed unexpectedly ({Reason}).", order.Id, kind, Summarize(ex));
        }
    }

    public async Task ScheduleDeliveryFollowUpAsync(Order order, CancellationToken cancellationToken = default)
    {
        try
        {
            var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
            if (numbers.Count == 0)
                return;

            var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
            var body = BuildBody(order, NotificationKind.DeliveryFollowUp);

            foreach (var contactNumber in numbers)
            {
                var notification = new Notification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp,
                    contactNumber.PhoneNumber, body, isScheduled: true, scheduledSendAt: sendAt);
                try
                {
                    // Scheduling is a provider feature: it requires a Messaging Service and ScheduleType=fixed.
                    var message = await _messaging.SendMessageAsync(new SendMessageCommand
                    {
                        To = contactNumber.PhoneNumber,
                        Body = body,
                        MessagingServiceSid = _configuration.MessagingServiceSid,
                        ScheduleType = "fixed",
                        SendAt = sendAt
                    }, cancellationToken);
                    notification.RecordProviderResult(message.Sid, message.Status, message.ErrorCode, message.ErrorMessage);
                }
                catch (Exception ex)
                {
                    notification.RecordSendFailure(Summarize(ex));
                    _logger.LogWarning("Order {OrderId}: the delivery follow-up could not be scheduled ({Reason}).", order.Id, Summarize(ex));
                }

                await _notifications.AddAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId}: scheduling the delivery follow-up failed unexpectedly ({Reason}).", order.Id, Summarize(ex));
        }
    }

    public async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var scheduled = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
            foreach (var notification in scheduled)
            {
                try
                {
                    var message = await _messaging.CancelMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                    notification.UpdateStatus(message.Status, message.ErrorCode, message.ErrorMessage);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                    _logger.LogInformation("Order {OrderId}: called off scheduled follow-up (notification {NotificationId}).", orderId, notification.Id);
                }
                catch (Exception ex)
                {
                    // Best-effort: log for operator visibility but don't fail the cancel operation.
                    _logger.LogWarning("Order {OrderId}: could not call off scheduled follow-up {NotificationId} ({Reason}).", orderId, notification.Id, Summarize(ex));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId}: calling off scheduled follow-ups failed unexpectedly ({Reason}).", orderId, Summarize(ex));
        }
    }

    public async Task RefreshStatusesAsync(IReadOnlyList<Notification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || NotificationStatus.IsTerminal(notification.Status))
                continue;

            try
            {
                var message = await _messaging.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                if (!string.Equals(message.Status, notification.Status, StringComparison.OrdinalIgnoreCase)
                    || message.ErrorCode != notification.ErrorCode)
                {
                    notification.UpdateStatus(message.Status, message.ErrorCode, message.ErrorMessage);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh delivery status for notification {NotificationId} ({Reason}).", notification.Id, Summarize(ex));
            }
        }
    }

    private static string BuildBody(Order order, NotificationKind kind)
    {
        var total = order.Total().ToString("0.00", CultureInfo.InvariantCulture);
        return kind switch
        {
            NotificationKind.OrderPlaced =>
                $"eShop: thanks! Your order #{order.Id} for ${total} has been placed.",
            NotificationKind.OrderDispatched =>
                $"eShop: good news - your order #{order.Id} is on its way!",
            NotificationKind.DeliveryFollowUp =>
                $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.",
            NotificationKind.OrderCancelled =>
                $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact us.",
            _ => $"eShop: an update about your order #{order.Id}."
        };
    }

    /// <summary>Turns an exception into a PII-free reason string safe to store and log.</summary>
    private static string Summarize(Exception ex) => ex switch
    {
        TwilioApiException tex => tex.TwilioCode is int code
            ? $"provider rejected the request (code {code})"
            : $"provider returned HTTP {(int)tex.StatusCode}",
        _ => "the provider could not be reached"
    };
}
