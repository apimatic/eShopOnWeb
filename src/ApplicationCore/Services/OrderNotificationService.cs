using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private static readonly Address DefaultShipTo = new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IOrderService _orderService;
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<ContactNumber> _contactRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<NotificationResendKey> _resendKeyRepository;
    private readonly ISmsNotificationGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IOrderService orderService,
        IRepository<Order> orderRepository,
        IRepository<ContactNumber> contactRepository,
        IRepository<OrderNotification> notificationRepository,
        IRepository<NotificationResendKey> resendKeyRepository,
        ISmsNotificationGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderService = orderService;
        _orderRepository = orderRepository;
        _contactRepository = contactRepository;
        _notificationRepository = notificationRepository;
        _resendKeyRepository = resendKeyRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        Address? shipTo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));

        var tuples = lines.Select(l => (l.CatalogItemId, l.Quantity)).ToList();
        var order = await _orderService.CreateOrderFromCatalogItemsAsync(
            buyerId,
            tuples,
            shipTo ?? DefaultShipTo);

        await NotifyDestinationsAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"eShopOnWeb: Your order #{order.Id} has been placed. Total: {order.Total():0.00}.",
            cancellationToken);

        return order;
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await RequireOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await NotifyDestinationsAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"eShopOnWeb: Order #{order.Id} is on its way.",
            cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await NotifyDestinationsAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: How did the delivery of order #{order.Id} go? Reply with your feedback.",
            cancellationToken,
            sendAt);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await RequireOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await NotifyDestinationsAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"eShopOnWeb: Order #{order.Id} has been cancelled.",
            cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
    }

    public Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrderAsync(
        int orderId,
        bool refreshFromProvider,
        CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByOrderIdSpecification(orderId),
            cancellationToken);

        if (refreshFromProvider)
        {
            foreach (var notification in notifications)
            {
                await RefreshFromProviderAsync(notification, cancellationToken);
            }
        }

        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existingKey = await _resendKeyRepository.FirstOrDefaultAsync(
            new ResendKeySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existingKey is not null)
        {
            var existing = await _notificationRepository.GetByIdAsync(existingKey.ResultNotificationId, cancellationToken);
            if (existing is not null)
            {
                await RefreshFromProviderAsync(existing, cancellationToken);
                return existing;
            }
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        await RefreshFromProviderAsync(original, cancellationToken);

        if (ReachedShopper(original.DeliveryStatus))
        {
            throw new OrderTransitionException("This message already reached the shopper and will not be resent.");
        }

        if (string.Equals(original.DeliveryStatus, "scheduled", StringComparison.OrdinalIgnoreCase))
        {
            throw new OrderTransitionException("A scheduled follow-up cannot be resent this way.");
        }

        if (string.IsNullOrWhiteSpace(original.Destination))
        {
            throw new OrderTransitionException("This notification has no destination on file.");
        }

        var body = original.BodyForDisplay()
            ?? $"eShopOnWeb: An update about order #{original.OrderId}.";

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            OrderNotificationKind.Resend,
            original.Destination,
            body,
            sourceNotificationId: original.Id);

        var snapshot = await TrySendNowAsync(original.Destination, body, cancellationToken);
        ApplySnapshot(resend, snapshot);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        await _resendKeyRepository.AddAsync(
            new NotificationResendKey(notificationId, idempotencyKey, resend.Id),
            cancellationToken);

        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            var snapshot = await _smsGateway.RedactBodyAsync(notification.ProviderSid, cancellationToken);
            ApplySnapshot(notification, snapshot);
        }

        notification.MarkRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' instant must be on or after 'from'.");
        }

        var providerPage = await _smsGateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var localWithSid = await _notificationRepository.ListAsync(
            new NotificationsWithProviderSidSpecification(),
            cancellationToken);

        var localBySid = localWithSid
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var entries = new List<ReconciliationEntry>();
        var seenSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in providerPage.Messages)
        {
            if (string.IsNullOrWhiteSpace(provider.ProviderSid))
            {
                continue;
            }

            seenSids.Add(provider.ProviderSid);
            localBySid.TryGetValue(provider.ProviderSid, out var local);
            entries.Add(new ReconciliationEntry
            {
                Match = local is null ? "provider-only" : "matched",
                ProviderSid = provider.ProviderSid,
                NotificationId = local?.Id,
                Status = provider.Status,
                DateSent = provider.DateSent
            });
        }

        foreach (var local in localWithSid)
        {
            if (string.IsNullOrWhiteSpace(local.ProviderSid) || seenSids.Contains(local.ProviderSid))
            {
                continue;
            }

            if (!InRange(local, from, to))
            {
                continue;
            }

            entries.Add(new ReconciliationEntry
            {
                Match = "eshop-only",
                ProviderSid = local.ProviderSid,
                NotificationId = local.Id,
                Status = local.DeliveryStatus,
                DateSent = local.DateSent
            });
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = providerPage.FromNumber,
            Complete = providerPage.Complete,
            Entries = entries
        };
    }

    private async Task NotifyDestinationsAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        CancellationToken cancellationToken,
        DateTimeOffset? sendAt = null)
    {
        var contacts = await _contactRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(order.BuyerId),
            cancellationToken);

        if (contacts.Count == 0)
        {
            _logger.LogInformation(
                "Skipping {Kind} SMS for order {OrderId}; buyer has no contact number on file.",
                kind,
                order.Id);
            return;
        }

        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                contact.CanonicalNumber,
                body,
                scheduledFor: sendAt);

            try
            {
                SmsMessageSnapshot snapshot = sendAt is DateTimeOffset at
                    ? await _smsGateway.ScheduleAsync(contact.CanonicalNumber, body, at, cancellationToken)
                    : await _smsGateway.SendNowAsync(contact.CanonicalNumber, body, cancellationToken);

                ApplySnapshot(notification, snapshot);
            }
            catch (Exception ex) when (ex is SmsProviderException or OperationCanceledException)
            {
                notification.MarkSendFailed(ex is SmsProviderException spe
                    ? spe.Message
                    : "The SMS provider call was canceled.");
                _logger.LogWarning(
                    "SMS {Kind} for order {OrderId} did not complete. Notification will be stored as failed.",
                    kind,
                    order.Id);
            }
            catch (Exception)
            {
                notification.MarkSendFailed("The SMS provider call failed.");
                _logger.LogWarning(
                    "SMS {Kind} for order {OrderId} failed unexpectedly. The order operation still succeeded.",
                    kind,
                    order.Id);
            }

            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notificationRepository.ListAsync(
            new FollowUpNotificationsByOrderIdSpecification(orderId),
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
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Could not cancel scheduled follow-up notification {NotificationId} for order {OrderId}.",
                    followUp.Id,
                    orderId);
            }
        }
    }

    private async Task RefreshFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            return;
        }

        try
        {
            var snapshot = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
            ApplySnapshot(notification, snapshot);
            if (notification.ContentRedacted)
            {
                notification.MarkRedacted();
            }
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "Could not refresh provider status for notification {NotificationId}.",
                notification.Id);
        }
    }

    private async Task<SmsMessageSnapshot> TrySendNowAsync(string to, string body, CancellationToken cancellationToken)
    {
        try
        {
            return await _smsGateway.SendNowAsync(to, body, cancellationToken);
        }
        catch (SmsProviderException ex)
        {
            return new SmsMessageSnapshot
            {
                Status = "failed",
                ErrorMessage = ex.Message,
                OutcomeUnknown = ex.Kind == SmsProviderFailureKind.OutcomeUnknown
            };
        }
    }

    private async Task<Order> RequireOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        return order ?? throw new OrderNotFoundException(orderId);
    }

    private static void ApplySnapshot(OrderNotification notification, SmsMessageSnapshot snapshot)
    {
        if (snapshot.OutcomeUnknown && string.IsNullOrWhiteSpace(snapshot.ProviderSid))
        {
            notification.MarkSendFailed("The provider outcome is unknown; the message may or may not have been accepted.");
            return;
        }

        notification.ApplyProviderState(
            snapshot.ProviderSid,
            string.IsNullOrWhiteSpace(snapshot.Status) ? "unknown" : snapshot.Status,
            snapshot.Body,
            snapshot.To,
            snapshot.DateSent,
            snapshot.ErrorCode,
            snapshot.ErrorMessage);
    }

    private static bool ReachedShopper(string status) =>
        status.Equals("delivered", StringComparison.OrdinalIgnoreCase)
        || status.Equals("read", StringComparison.OrdinalIgnoreCase)
        || status.Equals("received", StringComparison.OrdinalIgnoreCase);

    private static bool InRange(OrderNotification notification, DateTimeOffset from, DateTimeOffset to)
    {
        if (TryParseProviderDate(notification.DateSent, out var dateSent))
        {
            return dateSent >= from && dateSent <= to;
        }

        return notification.CreatedAt >= from && notification.CreatedAt <= to;
    }

    private static bool TryParseProviderDate(string? value, out DateTimeOffset parsed)
    {
        if (!string.IsNullOrWhiteSpace(value) && DateTimeOffset.TryParse(value, out parsed))
        {
            return true;
        }

        parsed = default;
        return false;
    }
}
