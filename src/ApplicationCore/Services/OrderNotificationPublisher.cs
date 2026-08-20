using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationPublisher
{
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsNotificationGateway _gateway;
    private readonly IAppLogger<OrderNotificationPublisher> _logger;

    public OrderNotificationPublisher(
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsNotificationGateway gateway,
        IAppLogger<OrderNotificationPublisher> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<string?> ResolveActiveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ShopperContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.CanonicalNumber;
    }

    public async Task<bool> IsDestinationStillRegisteredAsync(string buyerId, string destination, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ShopperContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers.Any(n => n.CanonicalNumber == destination);
    }

    public async Task<OrderNotification?> PublishAsync(
        Order order,
        string kind,
        string body,
        DateTimeOffset? sendAt,
        int? sourceNotificationId,
        CancellationToken cancellationToken)
    {
        Guard.Against.Null(order, nameof(order));

        string? destination;
        try
        {
            destination = await ResolveActiveDestinationAsync(order.BuyerId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Failed to resolve a destination for order {OrderId}: {Error}", order.Id, ex.GetType().Name);
            return null;
        }

        if (string.IsNullOrEmpty(destination))
        {
            return null;
        }

        return await SendToDestinationAsync(order, kind, body, destination, sendAt, sourceNotificationId, cancellationToken);
    }

    public async Task<OrderNotification> SendToDestinationAsync(
        Order order,
        string kind,
        string body,
        string destination,
        DateTimeOffset? sendAt,
        int? sourceNotificationId,
        CancellationToken cancellationToken)
    {
        SmsSendResult result;
        try
        {
            result = sendAt.HasValue
                ? await _gateway.ScheduleAsync(destination, body, sendAt.Value, cancellationToken)
                : await _gateway.SendNowAsync(destination, body, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("SMS send failed for order {OrderId} kind {Kind}: {Error}", order.Id, kind, ex.GetType().Name);
            result = new SmsSendResult(true, null, "failed", null, "The message could not be sent.", false);
        }

        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            kind,
            destination,
            body,
            result.ProviderSid,
            result.Status ?? (result.OutcomeUnknown ? "unknown" : "failed"),
            sendAt,
            sourceNotificationId,
            result.ErrorCode,
            result.ErrorMessage);

        return await _notifications.AddAsync(notification, cancellationToken);
    }

    public async Task RefreshProviderStateAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderSid))
        {
            return;
        }

        try
        {
            var snapshot = await _gateway.FetchAsync(notification.ProviderSid, cancellationToken);
            if (snapshot is null)
            {
                return;
            }

            notification.ApplyProviderState(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Failed to refresh notification {NotificationId}: {Error}", notification.Id, ex.GetType().Name);
        }
    }
}
