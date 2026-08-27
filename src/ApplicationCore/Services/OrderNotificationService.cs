using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // Provider-held schedule: the follow-up is queued with the provider this long after dispatch.
    private static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IMessagingProvider _messagingProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IMessagingProvider messagingProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _messagingProvider = messagingProvider;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{order.Id} has been placed. Total: {order.Total().ToString("C", CultureInfo.GetCultureInfo("en-US"))}. Thank you for shopping with us!";
        await SendToShopperNumbersAsync(order, NotificationType.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: good news! Your order #{order.Id} has been dispatched and is on its way.";
        await SendToShopperNumbersAsync(order, NotificationType.OrderDispatched, body, cancellationToken);

        var followUpBody = $"eShop: your order #{order.Id} should have arrived by now. How did the delivery go? Reply and let us know.";
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await ScheduleForShopperNumbersAsync(order, NotificationType.DeliveryFollowUp, followUpBody, sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{order.Id} has been cancelled. If you did not request this, please contact support.";
        await SendToShopperNumbersAsync(order, NotificationType.OrderCancelled, body, cancellationToken);

        // A follow-up that has not yet gone out must never reach a cancelled order's shopper.
        var scheduledSpec = new ScheduledFollowUpsByOrderSpecification(order.Id);
        var scheduledFollowUps = await _notificationRepository.ListAsync(scheduledSpec, cancellationToken);
        foreach (var followUp in scheduledFollowUps)
        {
            try
            {
                var providerMessage = await _messagingProvider.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateProviderState(providerMessage.Status ?? "canceled", providerMessage.ErrorCode, providerMessage.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {NotificationId} at the provider. Provider error type: {ErrorType}.", followUp.Id, ex.GetType().Name);
            }
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    public async Task RefreshStatusesAsync(IReadOnlyCollection<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications.Where(n => n.ProviderMessageSid != null))
        {
            try
            {
                var providerMessage = await _messagingProvider.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                if (providerMessage != null)
                {
                    notification.UpdateProviderState(providerMessage.Status ?? notification.Status, providerMessage.ErrorCode, providerMessage.ErrorMessage);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh provider status for notification {NotificationId}. Provider error type: {ErrorType}.", notification.Id, ex.GetType().Name);
            }
        }
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existingSpec = new NotificationByIdempotencyKeySpecification(idempotencyKey);
        var existing = await _notificationRepository.FirstOrDefaultAsync(existingSpec, cancellationToken);
        if (existing != null)
        {
            _logger.LogInformation("Idempotency key already used; returning notification {NotificationId} without re-sending.", existing.Id);
            return existing;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            return null;
        }

        if (original.ContentRedacted || original.Body == null)
        {
            throw new InvalidOperationException("The content of this message has been disposed of and can no longer be re-sent.");
        }

        // A removed contact number must never be messaged again.
        var numbersSpec = new ContactNumbersByBuyerSpecification(original.BuyerId);
        var registeredNumbers = await _contactNumberRepository.ListAsync(numbersSpec, cancellationToken);
        if (!registeredNumbers.Any(n => n.PhoneNumber == original.ToNumber))
        {
            throw new InvalidOperationException("The destination number is no longer registered for this shopper.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ToNumber, original.Body, original.Type, idempotencyKey);
        await _notificationRepository.AddAsync(resend, cancellationToken);
        await SendAndRecordAsync(resend, cancellationToken);
        return resend;
    }

    public async Task<OrderNotification?> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return null;
        }

        if (!notification.ContentRedacted)
        {
            if (notification.ProviderMessageSid != null)
            {
                await _messagingProvider.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
            }
            notification.RedactContent();
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }

        return notification;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _messagingProvider.ListMessagesAsync(from, to, cancellationToken);

        var localSpec = new NotificationsCreatedBetweenSpecification(from, to);
        var localNotifications = await _notificationRepository.ListAsync(localSpec, cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid != null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));

        var entries = new List<ReconciliationEntry>();
        var matched = 0;

        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.Sid, out var local))
            {
                matched++;
                entries.Add(new ReconciliationEntry(message.Sid, local.Id, message.Status, message.DateSent ?? message.DateCreated, "matched"));
            }
            else
            {
                entries.Add(new ReconciliationEntry(message.Sid, null, message.Status, message.DateSent ?? message.DateCreated, "providerOnly"));
            }
        }

        foreach (var notification in localNotifications)
        {
            if (notification.ProviderMessageSid == null || !providerSids.Contains(notification.ProviderMessageSid))
            {
                entries.Add(new ReconciliationEntry(notification.ProviderMessageSid, notification.Id, notification.Status, notification.CreatedAt, "eshopOnly"));
            }
        }

        return new ReconciliationReport(from, to, providerMessages.Count, localNotifications.Count, matched, entries);
    }

    private async Task SendToShopperNumbersAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var numbers = await GetShopperNumbersAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, number.PhoneNumber, body, type);
            await _notificationRepository.AddAsync(notification, cancellationToken);
            await SendAndRecordAsync(notification, cancellationToken);
        }
    }

    private async Task ScheduleForShopperNumbersAsync(Order order, NotificationType type, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var numbers = await GetShopperNumbersAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, number.PhoneNumber, body, type);
            await _notificationRepository.AddAsync(notification, cancellationToken);
            try
            {
                var providerMessage = await _messagingProvider.ScheduleMessageAsync(number.PhoneNumber, body, sendAt, cancellationToken);
                notification.MarkAccepted(providerMessage.Sid, providerMessage.Status ?? "scheduled", sendAt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to schedule notification {NotificationId} for order {OrderId}. Provider error type: {ErrorType}.", notification.Id, order.Id, ex.GetType().Name);
                notification.MarkRejected("failed", null, "Scheduling with the provider failed.");
            }
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task SendAndRecordAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var providerMessage = await _messagingProvider.SendMessageAsync(notification.ToNumber, notification.Body!, cancellationToken);
            notification.MarkAccepted(providerMessage.Sid, providerMessage.Status ?? "queued");
            notification.UpdateProviderState(providerMessage.Status ?? "queued", providerMessage.ErrorCode, providerMessage.ErrorMessage);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogWarning("Failed to send notification {NotificationId} for order {OrderId}. Provider error type: {ErrorType}.", notification.Id, notification.OrderId, ex.GetType().Name);
            notification.MarkRejected("failed", null, "Sending via the provider failed.");
        }
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task<List<ContactNumber>> GetShopperNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var spec = new ContactNumbersByBuyerSpecification(buyerId);
        return await _contactNumberRepository.ListAsync(spec, cancellationToken);
    }
}
