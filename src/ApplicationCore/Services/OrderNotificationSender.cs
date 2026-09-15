using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationSender : IOrderNotificationSender
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsMessageGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationSender> _logger;

    public OrderNotificationSender(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsMessageGateway smsGateway,
        IAppLogger<OrderNotificationSender> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<OrderNotification?> TryNotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt = null,
        int? sourceNotificationId = null,
        CancellationToken cancellationToken = default)
    {
        var destination = await GetCurrentDestinationAsync(order.BuyerId, cancellationToken);
        if (destination == null)
        {
            _logger.LogInformation("Skipping SMS for order {OrderId} because the shopper has no contact number on file.", order.Id);
            return null;
        }

        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            kind,
            body,
            destination.CanonicalNumber,
            destination.Id,
            sourceNotificationId,
            sendAt);

        try
        {
            var result = await _smsGateway.SendAsync(
                new SmsSendRequest(destination.CanonicalNumber, body, sendAt),
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(result.Sid))
            {
                notification.RecordProviderAcceptance(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
            }
            else
            {
                notification.RecordSendFailure(result.ErrorMessage ?? "The messaging provider did not return a message identifier.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to send SMS for order {OrderId} notification kind {Kind}: {Message}", order.Id, kind, ex.Message);
            notification.RecordSendFailure("The messaging provider could not accept the message.");
        }

        return await _notifications.AddAsync(notification, cancellationToken);
    }

    public async Task SyncFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var result = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(result.Status, result.ErrorCode, result.ErrorMessage);
            if (notification.ContentRedacted)
            {
                notification.RedactContent();
            }
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to refresh provider status for notification {NotificationId}: {Message}", notification.Id, ex.Message);
        }
    }

    private async Task<ContactNumber?> GetCurrentDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId, newestFirst: true), cancellationToken);
        return numbers.Count == 0 ? null : numbers[0];
    }
}
