using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OperatorNotificationService : IOperatorNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<NotificationResendRecord> _resendRepository;
    private readonly IRepository<ShopperContactNumber> _contactRepository;
    private readonly ISmsNotificationGateway _gateway;
    private readonly ShopperOrderService _shopperOrders;
    private readonly IAppLogger<OperatorNotificationService> _logger;

    public OperatorNotificationService(
        IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        IRepository<NotificationResendRecord> resendRepository,
        IRepository<ShopperContactNumber> contactRepository,
        ISmsNotificationGateway gateway,
        ShopperOrderService shopperOrders,
        IAppLogger<OperatorNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _resendRepository = resendRepository;
        _contactRepository = contactRepository;
        _gateway = gateway;
        _shopperOrders = shopperOrders;
        _logger = logger;
    }

    public async Task<Result<Order>> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Result<Order>.NotFound();
        }

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            return AppResult.Conflict<Order>(ex.Message);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);

        await _shopperOrders.NotifyAsync(order, NotificationKinds.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.",
            sendAt: null, cancellationToken);

        await _shopperOrders.NotifyAsync(order, NotificationKinds.DeliveryFollowUp,
            $"How did the delivery of eShop order #{order.Id} go?",
            sendAt: DateTimeOffset.UtcNow.Add(FollowUpDelay), cancellationToken);

        return Result<Order>.Success(order);
    }

    public async Task<Result<Order>> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Result<Order>.NotFound();
        }

        var followUps = await _notificationRepository.ListAsync(new FollowUpNotificationsByOrderIdSpec(orderId), cancellationToken);
        foreach (var followUp in followUps.Where(f => !string.IsNullOrEmpty(f.ProviderSid)))
        {
            var snapshot = await _gateway.CancelScheduledAsync(followUp.ProviderSid!, cancellationToken);
            if (snapshot is null)
            {
                await _shopperOrders.RefreshStatusAsync(followUp, cancellationToken);
                if (IsStillScheduled(followUp.Status))
                {
                    _logger.LogWarning("Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}.", followUp.Id, orderId);
                    return Result<Order>.Error("The scheduled follow-up message could not be cancelled. The order was not cancelled.");
                }
            }
            else
            {
                followUp.ApplyProviderResult(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Direction);
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await _shopperOrders.NotifyAsync(order, NotificationKinds.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            sendAt: null, cancellationToken);

        return Result<Order>.Success(order);
    }

    public async Task<Result<OrderNotification>> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return AppResult.Invalid<OrderNotification>("An idempotency key is required.");
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            return Result<OrderNotification>.NotFound();
        }

        var existing = await _resendRepository.FirstOrDefaultAsync(
            new ResendRecordByKeySpec(notificationId, idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            var previous = await _notificationRepository.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (previous is not null)
            {
                return Result<OrderNotification>.Success(previous);
            }
        }

        if (string.IsNullOrWhiteSpace(source.Destination))
        {
            return AppResult.Conflict<OrderNotification>("The original message has no destination.");
        }

        var stillRegistered = await _contactRepository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpec(source.BuyerId, source.Destination), cancellationToken);
        if (stillRegistered is null)
        {
            return AppResult.Conflict<OrderNotification>("The destination is no longer on file and cannot be messaged again.");
        }

        if (source.ContentDisposed || string.IsNullOrWhiteSpace(source.Body))
        {
            return AppResult.Conflict<OrderNotification>("The message content is no longer available to resend.");
        }

        var resend = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            NotificationKinds.Resend,
            source.Destination,
            source.Body,
            scheduledFor: null,
            sourceNotificationId: source.Id);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        var snapshot = await _gateway.TrySendAsync(new SmsSendRequest(source.Destination, source.Body, SendAt: null), cancellationToken);
        if (string.IsNullOrEmpty(snapshot.Sid))
        {
            resend.MarkSendFailed(snapshot.ErrorMessage ?? "The message could not be sent.");
        }
        else
        {
            resend.ApplyProviderResult(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Direction);
        }

        await _notificationRepository.UpdateAsync(resend, cancellationToken);
        await _resendRepository.AddAsync(new NotificationResendRecord(source.Id, idempotencyKey, resend.Id), cancellationToken);

        _logger.LogInformation("Resent notification {SourceNotificationId} as {NotificationId}.", source.Id, resend.Id);
        return Result<OrderNotification>.Success(resend);
    }

    public async Task<Result> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return Result.NotFound();
        }

        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            var snapshot = await _gateway.RedactBodyAsync(notification.ProviderSid, cancellationToken);
            if (snapshot is null)
            {
                return Result.Error("The provider could not dispose of the message content.");
            }
        }

        notification.DisposeContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed content for notification {NotificationId}.", notificationId);
        return Result.Success();
    }

    public async Task<Result<ReconciliationReport>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            return AppResult.Invalid<ReconciliationReport>("'to' must be on or after 'from'.");
        }

        IReadOnlyList<SmsMessageSnapshot> providerMessages;
        try
        {
            providerMessages = await _gateway.ListFromConfiguredNumberAsync(from, to, cancellationToken);
        }
        catch (Exceptions.SmsProviderException)
        {
            return Result<ReconciliationReport>.Error("The messaging provider is unavailable.");
        }

        var providerSids = providerMessages
            .Select(m => m.Sid)
            .Where(sid => !string.IsNullOrEmpty(sid))
            .Cast<string>()
            .Distinct()
            .ToList();

        var bySid = providerSids.Count == 0
            ? new List<OrderNotification>()
            : await _notificationRepository.ListAsync(new NotificationsByProviderSidsSpec(providerSids), cancellationToken);
        var byTime = await _notificationRepository.ListAsync(new NotificationsInRangeSpec(from, to), cancellationToken);

        var application = bySid.Concat(byTime)
            .GroupBy(n => n.Id)
            .Select(g => g.First())
            .ToList();

        var applicationSids = application
            .Select(n => n.ProviderSid)
            .Where(sid => !string.IsNullOrEmpty(sid))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        var providerSidSet = providerSids.ToHashSet(StringComparer.Ordinal);

        var onlyInProvider = providerSidSet.Except(applicationSids).OrderBy(s => s).ToList();
        var onlyInApplication = applicationSids.Except(providerSidSet).OrderBy(s => s).ToList();
        var matched = providerSidSet.Intersect(applicationSids).OrderBy(s => s).ToList();

        var fromNumber = _gateway.ConfiguredFromNumber;

        return Result<ReconciliationReport>.Success(new ReconciliationReport(
            from, to, fromNumber, providerMessages, application, onlyInProvider, onlyInApplication, matched));
    }

    private static bool IsStillScheduled(string? status) =>
        string.Equals(status, "scheduled", StringComparison.OrdinalIgnoreCase);
}
