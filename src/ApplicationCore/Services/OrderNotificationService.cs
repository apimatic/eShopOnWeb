using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using TwilioSettings = Microsoft.eShopWeb.TwilioSettings;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>
    /// Follow-up is queued with Twilio a few days after dispatch (must be between 15 minutes and 35 days).
    /// </summary>
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<NotificationResendReceipt> _resendRepository;
    private readonly IContactNumberService _contactNumberService;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly TwilioSettings _twilioSettings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        IRepository<NotificationResendReceipt> resendRepository,
        IContactNumberService contactNumberService,
        ITwilioMessagingClient messagingClient,
        TwilioSettings twilioSettings,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _resendRepository = resendRepository;
        _contactNumberService = contactNumberService;
        _messagingClient = messagingClient;
        _twilioSettings = twilioSettings;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
        => NotifyShopperAsync(
            order,
            OrderNotificationType.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.",
            sendAt: null,
            cancellationToken);

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderFulfillmentStatus.Cancelled)
        {
            throw new BadRequestException("A cancelled order cannot be dispatched.");
        }

        var alreadyDispatched = order.Status == OrderFulfillmentStatus.Dispatched;
        if (!alreadyDispatched)
        {
            order.MarkDispatched();
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        if (alreadyDispatched)
        {
            return;
        }

        await NotifyShopperAsync(
            order,
            OrderNotificationType.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await NotifyShopperAsync(
            order,
            OrderNotificationType.DeliveryFollowUp,
            $"How did the delivery of your eShopOnWeb order #{order.Id} go?",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        var alreadyCancelled = order.Status == OrderFulfillmentStatus.Cancelled;
        if (!alreadyCancelled)
        {
            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        await CancelPendingFollowUpsAsync(orderId, cancellationToken);

        if (alreadyCancelled)
        {
            return;
        }

        await NotifyShopperAsync(
            order,
            OrderNotificationType.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new NotFoundException("Order not found.");
        }

        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByOrderSpecification(orderId),
            cancellationToken);

        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new BadRequestException("An idempotency key is required.");
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new NotFoundException("Notification not found.");
        }

        var existingReceipt = await _resendRepository.FirstOrDefaultAsync(
            new ResendReceiptByKeySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existingReceipt is not null)
        {
            var previous = await _notificationRepository.GetByIdAsync(existingReceipt.ResultNotificationId, cancellationToken);
            if (previous is not null)
            {
                await RefreshFromProviderAsync(new[] { previous }, cancellationToken);
                return previous;
            }
        }

        var body = original.Body ?? BodyForType(original.Type, original.OrderId);
        var destination = await ResolveResendDestinationAsync(original, cancellationToken);

        var resent = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Type,
            body,
            destination ?? original.Destination,
            scheduledAt: null,
            resentFromNotificationId: original.Id);

        if (destination is null)
        {
            resent.MarkSendFailed("No contact number on file.");
            await _notificationRepository.AddAsync(resent, cancellationToken);
        }
        else
        {
            await SendAndPersistAsync(resent, sendAt: null, cancellationToken);
        }

        await _resendRepository.AddAsync(
            new NotificationResendReceipt(original.Id, idempotencyKey, resent.Id),
            cancellationToken);

        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new NotFoundException("Notification not found.");
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var snapshot = await _messagingClient.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderResult(
                    snapshot.Sid,
                    snapshot.Status,
                    snapshot.ErrorCode,
                    PhoneNumberSanitizer.Redact(snapshot.ErrorMessage),
                    snapshot.DateSent,
                    snapshot.Body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to redact provider content for notification {NotificationId}: {Error}",
                    notification.Id,
                    PhoneNumberSanitizer.Redact(ex.Message));
                throw;
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new BadRequestException("'to' must be on or after 'from'.");
        }

        var providerMessages = await _messagingClient.ListMessagesFromSenderAsync(from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        var localInRange = await _notificationRepository.ListAsync(
            new NotificationsCreatedInRangeSpecification(from, to),
            cancellationToken);

        IReadOnlyList<OrderNotification> localBySid = Array.Empty<OrderNotification>();
        if (providerBySid.Count > 0)
        {
            localBySid = await _notificationRepository.ListAsync(
                new NotificationsByProviderSidsSpecification(providerBySid.Keys),
                cancellationToken);
        }

        var localById = localInRange.Concat(localBySid).GroupBy(n => n.Id).Select(g => g.First()).ToList();
        var localByProviderSid = localById
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<ReconciliationProviderMessage>();
        var applicationOnly = new List<ReconciliationApplicationMessage>();

        foreach (var provider in providerBySid.Values)
        {
            if (localByProviderSid.TryGetValue(provider.Sid, out var local))
            {
                matched.Add(new ReconciliationMatch(local.Id, provider.Sid, local.ProviderStatus, provider.Status));
            }
            else
            {
                providerOnly.Add(new ReconciliationProviderMessage(
                    provider.Sid,
                    provider.Status,
                    provider.DateSent,
                    provider.Body));
            }
        }

        foreach (var local in localById)
        {
            if (string.IsNullOrEmpty(local.ProviderMessageSid) || !providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                applicationOnly.Add(new ReconciliationApplicationMessage(
                    local.Id,
                    local.ProviderMessageSid,
                    local.ProviderStatus,
                    local.CreatedAt));
            }
        }

        return new ReconciliationReport(
            from,
            to,
            _twilioSettings.FromNumber,
            matched,
            providerOnly,
            applicationOnly);
    }

    private async Task NotifyShopperAsync(
        Order order,
        OrderNotificationType type,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Entities.ContactAggregate.ShopperContactNumber> numbers;
        try
        {
            numbers = await _contactNumberService.ListActiveAsync(order.BuyerId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Could not load contact numbers while notifying order {OrderId}: {Error}",
                order.Id,
                PhoneNumberSanitizer.Redact(ex.Message));
            return;
        }

        if (numbers.Count == 0)
        {
            _logger.LogInformation("No contact number on file; skipping {Type} notification for order {OrderId}", type, order.Id);
            return;
        }

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, type, body, number.PhoneNumber, sendAt);
            await SendAndPersistAsync(notification, sendAt, cancellationToken);
        }
    }

    private async Task SendAndPersistAsync(
        OrderNotification notification,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _messagingClient.CreateMessageAsync(
                notification.Destination,
                notification.Body ?? string.Empty,
                sendAt,
                cancellationToken);

            notification.ApplyProviderResult(
                snapshot.Sid,
                snapshot.Status,
                snapshot.ErrorCode,
                PhoneNumberSanitizer.Redact(snapshot.ErrorMessage),
                snapshot.DateSent,
                providerBody: null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to send {Type} notification for order {OrderId}: {Error}",
                notification.Type,
                notification.OrderId,
                PhoneNumberSanitizer.Redact(ex.Message));
            notification.MarkSendFailed(PhoneNumberSanitizer.Redact(ex.Message));
        }

        if (notification.Id == 0)
        {
            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
        else
        {
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notificationRepository.ListAsync(
            new ScheduledFollowUpNotificationsByOrderSpecification(orderId),
            cancellationToken);

        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid) || !followUp.IsScheduled)
            {
                continue;
            }

            try
            {
                var snapshot = await _messagingClient.CancelScheduledMessageAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.ApplyProviderResult(
                    snapshot.Sid,
                    snapshot.Status,
                    snapshot.ErrorCode,
                    PhoneNumberSanitizer.Redact(snapshot.ErrorMessage),
                    snapshot.DateSent,
                    snapshot.Body);
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not cancel scheduled follow-up {NotificationId} for order {OrderId}: {Error}",
                    followUp.Id,
                    orderId,
                    PhoneNumberSanitizer.Redact(ex.Message));
            }
        }
    }

    private async Task RefreshFromProviderAsync(
        IReadOnlyList<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messagingClient.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
                var body = notification.ContentRedacted ? null : snapshot.Body;
                notification.ApplyProviderResult(
                    snapshot.Sid,
                    snapshot.Status,
                    snapshot.ErrorCode,
                    PhoneNumberSanitizer.Redact(snapshot.ErrorMessage),
                    snapshot.DateSent,
                    body);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not refresh notification {NotificationId} from provider: {Error}",
                    notification.Id,
                    PhoneNumberSanitizer.Redact(ex.Message));
            }
        }
    }

    private async Task<string?> ResolveResendDestinationAsync(
        OrderNotification original,
        CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberService.ListActiveAsync(original.BuyerId, cancellationToken);
        if (numbers.Count == 0)
        {
            return null;
        }

        var originalStillRegistered = numbers.FirstOrDefault(n => n.PhoneNumber == original.Destination);
        return originalStillRegistered?.PhoneNumber ?? numbers[0].PhoneNumber;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken);
        if (order is null)
        {
            throw new NotFoundException("Order not found.");
        }

        return order;
    }

    private static string BodyForType(OrderNotificationType type, int orderId) => type switch
    {
        OrderNotificationType.OrderPlaced => $"Your eShopOnWeb order #{orderId} has been placed.",
        OrderNotificationType.OrderDispatched => $"Your eShopOnWeb order #{orderId} is on its way.",
        OrderNotificationType.DeliveryFollowUp => $"How did the delivery of your eShopOnWeb order #{orderId} go?",
        OrderNotificationType.OrderCancelled => $"Your eShopOnWeb order #{orderId} has been cancelled.",
        _ => $"Update for your eShopOnWeb order #{orderId}."
    };
}
