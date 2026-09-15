using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OperatorOrderNotificationService : IOperatorOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IOrderSmsNotifier _notifier;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OperatorOrderNotificationService> _logger;

    public OperatorOrderNotificationService(
        IRepository<Order> orders,
        IRepository<OrderNotification> notifications,
        IRepository<ShopperContactNumber> contactNumbers,
        IOrderSmsNotifier notifier,
        ISmsGateway smsGateway,
        IAppLogger<OperatorOrderNotificationService> logger)
    {
        _orders = orders;
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _notifier = notifier;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Order {orderId} was not found.");

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await _notifier.NotifyAsync(order.Id, order.BuyerId, OrderNotificationKind.OrderDispatched, cancellationToken);
        await _notifier.NotifyAsync(
            order.Id,
            order.BuyerId,
            OrderNotificationKind.DeliveryFollowUp,
            cancellationToken,
            DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay));

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Order {orderId} was not found.");

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await _notifier.CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await _notifier.NotifyAsync(order.Id, order.BuyerId, OrderNotificationKind.OrderCancelled, cancellationToken);

        return order;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpec(notificationId, idempotencyKey),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Notification {notificationId} was not found.");

        var registered = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(original.BuyerId), cancellationToken);
        if (!registered.Any(n => original.DestinationMatches(n.CanonicalNumber)))
        {
            throw new InvalidContactNumberException();
        }

        var body = original.BodyRedacted || string.IsNullOrWhiteSpace(original.Body)
            ? OrderSmsNotifier.BodyFor(original.Kind, original.OrderId)
            : original.Body;

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            body,
            original.DestinationCanonicalNumber,
            original.Id,
            idempotencyKey);

        try
        {
            var send = await _smsGateway.SendSmsAsync(original.DestinationCanonicalNumber, body!, cancellationToken);
            if (send.AcceptedByProvider)
            {
                resend.RecordProviderAcceptance(send.ProviderSid, send.Status, send.ErrorCode, send.ErrorMessage);
            }
            else
            {
                resend.RecordSendFailure(send.ErrorMessage ?? "The provider did not accept the message.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Resend of notification {NotificationId} failed with {ExceptionType}", original.Id, ex.GetType().Name);
            throw new SmsProviderException("The provider could not send the message.", inner: ex);
        }

        return await _notifications.AddAsync(resend, cancellationToken);
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Notification {notificationId} was not found.");

        if (!string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            await _smsGateway.RedactBodyAsync(notification.ProviderSid, cancellationToken);

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var snapshot = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
                if (snapshot is null || string.IsNullOrEmpty(snapshot.Body))
                {
                    break;
                }

                if (attempt == 2)
                {
                    throw new SmsProviderException("The provider did not dispose of the message content.");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
            }
        }

        notification.MarkBodyRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var providerMessages = await _smsGateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsInCreatedRangeSpec(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationMessage>();
        var providerOnly = new List<ReconciliationMessage>();
        var eshopOnly = new List<ReconciliationMessage>();

        foreach (var (sid, provider) in providerBySid)
        {
            if (localBySid.TryGetValue(sid, out var ours))
            {
                matched.Add(ToMessage(ours, provider, "matched"));
            }
            else
            {
                providerOnly.Add(new ReconciliationMessage(
                    null,
                    provider.Sid,
                    provider.Status,
                    provider.Body,
                    provider.DateCreated,
                    "provider"));
            }
        }

        foreach (var ours in local)
        {
            if (string.IsNullOrWhiteSpace(ours.ProviderSid) || !providerBySid.ContainsKey(ours.ProviderSid))
            {
                eshopOnly.Add(ToMessage(ours, null, "eshop"));
            }
        }

        return new ReconciliationReport(from, to, matched, providerOnly, eshopOnly);
    }

    private static ReconciliationMessage ToMessage(OrderNotification ours, SmsMessageSnapshot? provider, string source) =>
        new(
            ours.Id.ToString(),
            provider?.Sid ?? ours.ProviderSid,
            provider?.Status ?? ours.Status,
            ours.BodyRedacted ? null : provider?.Body ?? ours.Body,
            provider?.DateCreated ?? ours.CreatedAt.ToString("O"),
            source);
}
