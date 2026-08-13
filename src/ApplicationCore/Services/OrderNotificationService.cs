using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates order-lifecycle SMS notifications on top of <see cref="ISmsProvider"/>. Sending is always
/// best-effort: a message that cannot be sent is recorded as such but never fails the order operation.
/// No phone number or message body is ever written to a log from here.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far ahead the "how did delivery go" follow-up is queued with the provider.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<OrderNotification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        IUriComposer uriComposer,
        ISmsProvider smsProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _uriComposer = uriComposer;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity < 1)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be at least 1.", nameof(lines));
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(lines));
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);

        await SendImmediateAsync(order, OrderNotificationType.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed. Thank you for shopping with us!", cancellationToken);

        return order.Id;
    }

    public async Task DispatchOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));

        await SendImmediateAsync(order, OrderNotificationType.Dispatched,
            $"Good news! Your eShop order #{order.Id} is on its way.", cancellationToken);

        await ScheduleFollowUpAsync(order, cancellationToken);
    }

    public async Task CancelOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));

        // Call off any not-yet-sent follow-up FIRST, so it can never reach the shopper.
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderIdSpecification(order.Id), cancellationToken);

        foreach (var followUp in notifications.Where(n =>
                     n.Type == OrderNotificationType.DeliveryFollowUp
                     && n.ProviderMessageSid is not null
                     && n.Status == MessageDeliveryStatus.Scheduled))
        {
            try
            {
                await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.MarkCanceled();
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                // Surface loudly in logs (no PII) — a follow-up that survives is an incident.
                _logger.LogWarning("Failed to cancel scheduled follow-up notification {NotificationId} for order {OrderId}: {Error}.",
                    followUp.Id, order.Id, ex.GetType().Name);
            }
        }

        await SendImmediateAsync(order, OrderNotificationType.Cancelled,
            $"Your eShop order #{order.Id} has been cancelled. If this is unexpected, please contact us.", cancellationToken);
    }

    public async Task<int> ResendAsync(OrderNotification original, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(original, nameof(original));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: if this key already produced a message, return it without sending again.
        var alreadyDone = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyDone is not null)
        {
            return alreadyDone.Id;
        }

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            throw new InvalidOperationException("The content of this message has been disposed of and cannot be re-sent.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, OrderNotificationType.Resend,
            original.ToNumber, original.Body);
        resend.SetIdempotencyKey(idempotencyKey);
        resend.SetResendOf(original.Id);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        try
        {
            var result = await _smsProvider.SendAsync(original.ToNumber, original.Body, cancellationToken);
            resend.MarkSubmitted(result.Sid, result.Status, result.ErrorCode, DateTimeOffset.UtcNow);
        }
        catch (SmsProviderException ex)
        {
            resend.MarkSubmissionFailed(ex.TwilioErrorCode);
            _logger.LogWarning("Provider rejected resend of notification {OriginalId} (twilio {Code}, http {Http}).",
                original.Id, ex.TwilioErrorCode, ex.HttpStatusCode);
        }
        catch (Exception ex)
        {
            resend.MarkSubmissionFailed(null);
            _logger.LogWarning("Failed to submit resend of notification {OriginalId}: {Error}.", original.Id, ex.GetType().Name);
        }

        await _notificationRepository.UpdateAsync(resend, cancellationToken);
        return resend.Id;
    }

    public async Task DisposeContentAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(notification, nameof(notification));

        // Dispose at the provider first; only then clear locally, so we never claim the content is gone
        // from the provider when it is not. An error here is surfaced to the caller.
        if (notification.ProviderMessageSid is not null && !notification.ContentRedacted)
        {
            await _smsProvider.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshDeliveryStatusAsync(notifications, cancellationToken);
        return notifications;
    }

    private async Task RefreshDeliveryStatusAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        if (notifications is null)
        {
            return;
        }

        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || MessageDeliveryStatus.IsTerminal(notification.Status))
            {
                continue; // nothing handed off, or the outcome is already final and authoritative
            }

            try
            {
                var state = await _smsProvider.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (state is not null)
                {
                    notification.UpdateDeliveryStatus(state.Status, state.ErrorCode);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh status of notification {NotificationId}: {Error}.",
                    notification.Id, ex.GetType().Name);
            }
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for OUR sending number's messages over the range (it filters by From itself),
        // then keep only those whose send time falls precisely within [from, to].
        var providerStates = await _smsProvider.ListSentFromConfiguredSenderAsync(from, to, cancellationToken);
        var providerBySid = providerStates
            .Where(s => s.DateSent.HasValue && s.DateSent.Value >= from && s.DateSent.Value <= to)
            .GroupBy(s => s.Sid)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // What eShop believes it sent in the range.
        var eshopNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsSentInRangeSpecification(from, to), cancellationToken);
        var eshopBySid = eshopNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        foreach (var s in providerBySid.Values)
        {
            if (eshopBySid.TryGetValue(s.Sid, out var n))
            {
                matched.Add(new ReconciliationEntry(s.Sid, s.Status, s.ErrorCode, n.Id, n.OrderId, s.DateSent));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(s.Sid, s.Status, s.ErrorCode, null, null, s.DateSent));
            }
        }

        var eshopOnly = new List<ReconciliationEntry>();
        foreach (var n in eshopBySid.Values)
        {
            if (!providerBySid.ContainsKey(n.ProviderMessageSid!))
            {
                eshopOnly.Add(new ReconciliationEntry(n.ProviderMessageSid!, n.Status, n.ErrorCode, n.Id, n.OrderId, n.SentAt));
            }
        }

        return new ReconciliationReport(
            from, to, _smsProvider.ConfiguredSenderNumber,
            providerBySid.Count, eshopBySid.Count, matched.Count,
            matched, providerOnly, eshopOnly);
    }

    private async Task<OrderNotification?> SendImmediateAsync(Order order, OrderNotificationType type, string body,
        CancellationToken cancellationToken)
    {
        var toNumber = await GetActiveNumberAsync(order.BuyerId, cancellationToken);
        if (toNumber is null)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation("No contact number on file for order {OrderId}; skipping {Type} notification.", order.Id, type);
            return null;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, type, toNumber, body);
        await _notificationRepository.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _smsProvider.SendAsync(toNumber, body, cancellationToken);
            notification.MarkSubmitted(result.Sid, result.Status, result.ErrorCode, DateTimeOffset.UtcNow);
        }
        catch (SmsProviderException ex)
        {
            notification.MarkSubmissionFailed(ex.TwilioErrorCode);
            _logger.LogWarning("Provider rejected {Type} message for order {OrderId} (twilio {Code}, http {Http}).",
                type, order.Id, ex.TwilioErrorCode, ex.HttpStatusCode);
        }
        catch (Exception ex)
        {
            notification.MarkSubmissionFailed(null);
            _logger.LogWarning("Failed to submit {Type} message for order {OrderId}: {Error}.", type, order.Id, ex.GetType().Name);
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    private async Task ScheduleFollowUpAsync(Order order, CancellationToken cancellationToken)
    {
        var toNumber = await GetActiveNumberAsync(order.BuyerId, cancellationToken);
        if (toNumber is null)
        {
            return;
        }

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = $"How did the delivery of your eShop order #{order.Id} go? We'd love to hear your feedback.";

        var notification = new OrderNotification(order.Id, order.BuyerId, OrderNotificationType.DeliveryFollowUp, toNumber, body);
        await _notificationRepository.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _smsProvider.ScheduleAsync(toNumber, body, sendAt, cancellationToken);
            notification.MarkScheduled(result.Sid, result.Status, sendAt);
        }
        catch (SmsProviderException ex)
        {
            notification.MarkSubmissionFailed(ex.TwilioErrorCode);
            _logger.LogWarning("Provider rejected scheduled follow-up for order {OrderId} (twilio {Code}, http {Http}).",
                order.Id, ex.TwilioErrorCode, ex.HttpStatusCode);
        }
        catch (Exception ex)
        {
            notification.MarkSubmissionFailed(null);
            _logger.LogWarning("Failed to schedule follow-up for order {OrderId}: {Error}.", order.Id, ex.GetType().Name);
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task<string?> GetActiveNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.PhoneNumber;
    }
}
