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
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates order SMS notifications. The Notify* methods never throw: an order
/// operation must succeed even when a message cannot be sent. Operator actions
/// (resend, content disposal, reconciliation) surface provider failures to the caller.
/// Destination numbers are never logged.
/// </summary>
public class OrderNotificationService : INotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly IContactNumberService _contactNumberService;
    private readonly ISmsService _smsService;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        IContactNumberService contactNumberService,
        ISmsService smsService,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _contactNumberService = contactNumberService;
        _smsService = smsService;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default)
    {
        var contact = await _contactNumberService.GetPrimaryAsync(order.BuyerId, ct);
        if (contact is null)
        {
            _logger.LogInformation("Order {OrderId}: buyer has no contact number; no order-placed SMS sent.", order.Id);
            return;
        }

        var body = $"Your eShop order #{order.Id} has been placed. Total: ${order.Total():0.00}. We'll text you when it's on its way.";
        await SendAndRecordAsync(order, contact, NotificationType.OrderPlaced, body, null, ct);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default)
    {
        var contact = await _contactNumberService.GetPrimaryAsync(order.BuyerId, ct);
        if (contact is null)
        {
            _logger.LogInformation("Order {OrderId}: buyer has no contact number; no dispatch SMS sent.", order.Id);
            return;
        }

        var dispatchBody = $"Good news — your eShop order #{order.Id} is on its way!";
        await SendAndRecordAsync(order, contact, NotificationType.OrderDispatched, dispatchBody, null, ct);

        var followUpAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var followUpBody = $"How did the delivery of your eShop order #{order.Id} go? We'd love to hear about it.";
        await SendAndRecordAsync(order, contact, NotificationType.DeliveryFollowUp, followUpBody, followUpAt, ct);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default)
    {
        var contact = await _contactNumberService.GetPrimaryAsync(order.BuyerId, ct);
        if (contact is null)
        {
            _logger.LogInformation("Order {OrderId}: buyer has no contact number; no cancellation SMS sent.", order.Id);
        }
        else
        {
            var body = $"Your eShop order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
            await SendAndRecordAsync(order, contact, NotificationType.OrderCancelled, body, null, ct);
        }

        // A queued follow-up must never reach a customer whose order was cancelled.
        var scheduledFollowUps = await _notificationRepository.ListAsync(new ScheduledFollowUpsForOrderSpecification(order.Id), ct);
        foreach (var followUp in scheduledFollowUps)
        {
            try
            {
                await _smsService.CancelScheduledSmsAsync(followUp.ProviderMessageSid!, ct);
                followUp.UpdateProviderStatus(NotificationStatuses.Canceled, null, null);
                await _notificationRepository.UpdateAsync(followUp, ct);
            }
            catch (Exception ex)
            {
                // The cancellation of the order itself must still succeed; the next status
                // refresh reconciles the follow-up's actual provider state. Provider error
                // text is not logged — it can embed the destination number.
                _logger.LogWarning("Order {OrderId}: could not cancel scheduled follow-up {MessageSid} (provider status {ProviderStatus}).",
                    order.Id, followUp.ProviderMessageSid ?? "n/a", ProviderStatusOf(ex));
            }
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken ct = default)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsForOrderSpecification(orderId), ct);
        await RefreshPendingStatusesAsync(notifications, ct);
        return notifications;
    }

    public async Task RefreshStatusesForOrdersAsync(IEnumerable<int> orderIds, CancellationToken ct = default)
    {
        foreach (var orderId in orderIds)
        {
            await GetOrderNotificationsAsync(orderId, ct);
        }
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct)
            ?? throw new EntityNotFoundException($"Notification {notificationId} was not found.");

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new ResendByIdempotencyKeySpecification(notificationId, idempotencyKey), ct);
        if (existing is not null)
        {
            return existing;
        }

        if (original.ContentDisposed)
        {
            throw new BadRequestException($"Notification {notificationId} content has been disposed of and cannot be resent.");
        }

        var contact = await _contactNumberRepository.GetByIdAsync(original.ContactNumberId, ct)
            ?? throw new BadRequestException("The destination number is no longer registered; the message cannot be resent.");

        var resend = new OrderNotification(
            original.OrderId, original.BuyerId, original.ContactNumberId, original.Type,
            original.Body!, scheduledFor: null, resendOfNotificationId: notificationId, idempotencyKey: idempotencyKey);

        try
        {
            var result = await _smsService.SendSmsAsync(contact.PhoneNumber, original.Body!, ct);
            resend.MarkProviderAccepted(result.MessageSid, result.Status);
        }
        catch (SmsProviderException ex)
        {
            resend.MarkSendFailed(ex.Message);
            await _notificationRepository.AddAsync(resend, CancellationToken.None);
            throw;
        }

        await _notificationRepository.AddAsync(resend, ct);
        return resend;
    }

    public async Task DeleteContentAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct)
            ?? throw new EntityNotFoundException($"Notification {notificationId} was not found.");

        if (notification.ProviderMessageSid is not null)
        {
            await _smsService.RedactMessageBodyAsync(notification.ProviderMessageSid, ct);
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification, ct);
    }

    public async Task<NotificationReconciliationResult> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var providerList = await _smsService.ListSentMessagesAsync(fromUtc, toUtc, ct);
        var appNotifications = await _notificationRepository.ListAsync(new NotificationsInRangeSpecification(fromUtc, toUtc), ct);

        var providerBySid = providerList.Messages
            .Where(m => !string.IsNullOrEmpty(m.MessageSid))
            .GroupBy(m => m.MessageSid)
            .ToDictionary(g => g.Key, g => g.First());
        var appBySid = appNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciledNotification>();
        foreach (var (sid, notification) in appBySid)
        {
            if (providerBySid.TryGetValue(sid, out var providerMessage))
            {
                matched.Add(new ReconciledNotification(
                    notification.Id,
                    sid,
                    notification.Status,
                    providerMessage.Status,
                    string.Equals(notification.Status, providerMessage.Status, StringComparison.OrdinalIgnoreCase)));
            }
        }

        var providerOnly = providerBySid.Values
            .Where(m => !appBySid.ContainsKey(m.MessageSid))
            .OrderBy(m => m.DateSent)
            .ToList();

        var appOnly = appNotifications
            .Where(n => n.ProviderMessageSid is null || !providerBySid.ContainsKey(n.ProviderMessageSid))
            .ToList();

        return new NotificationReconciliationResult(
            fromUtc, toUtc, providerList.FromNumber, matched, providerOnly, appOnly, providerList.Truncated);
    }

    private static string ProviderStatusOf(Exception ex) =>
        (ex as SmsProviderException)?.ProviderStatusCode?.ToString() ?? "n/a";

    private async Task SendAndRecordAsync(Order order, ContactNumber contact, NotificationType type, string body, DateTimeOffset? scheduledFor, CancellationToken ct)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id, type, body, scheduledFor);
        try
        {
            var result = scheduledFor is null
                ? await _smsService.SendSmsAsync(contact.PhoneNumber, body, ct)
                : await _smsService.ScheduleSmsAsync(contact.PhoneNumber, body, scheduledFor.Value, ct);
            notification.MarkProviderAccepted(result.MessageSid, result.Status);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed(ex is SmsProviderException ? ex.Message : "The provider could not be reached.");
            // Provider error text is not logged — it can embed the destination number.
            _logger.LogWarning("Order {OrderId}: {NotificationType} SMS failed (provider status {ProviderStatus}).",
                order.Id, type, ProviderStatusOf(ex));
        }

        await _notificationRepository.AddAsync(notification, CancellationToken.None);
    }

    private async Task RefreshPendingStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || NotificationStatuses.IsTerminal(notification.Status))
            {
                continue;
            }

            try
            {
                var status = await _smsService.GetMessageStatusAsync(notification.ProviderMessageSid, ct);
                if (status.Status is not null && !string.Equals(status.Status, notification.Status, StringComparison.OrdinalIgnoreCase))
                {
                    notification.UpdateProviderStatus(status.Status, status.ErrorCode, status.ErrorMessage);
                    await _notificationRepository.UpdateAsync(notification, ct);
                }
            }
            catch (Exception ex)
            {
                // Keep the last known status; a provider read failure must not break the read.
                // Provider error text is not logged — it can embed the destination number.
                _logger.LogWarning("Notification {NotificationId}: status refresh failed (provider status {ProviderStatus}).",
                    notification.Id, ProviderStatusOf(ex));
            }
        }
    }
}
