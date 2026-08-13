using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Sends the messages that go out as an order moves. Every path here is best-effort: it never lets a
/// messaging failure escape to fail the order operation that triggered it, and a shopper with no
/// number on file is simply not messaged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far after dispatch the "how did the delivery go?" follow-up is queued for.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<SmsNotification> _notifications;
    private readonly ISmsGateway _gateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IReadRepository<ContactNumber> contactNumbers,
        IRepository<SmsNotification> notifications,
        ISmsGateway gateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        foreach (var number in await GetOwnerNumbersAsync(order.BuyerId, cancellationToken))
        {
            await SendOneAsync(order, NotificationKind.OrderPlaced, number, cancellationToken);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        foreach (var number in await GetOwnerNumbersAsync(order.BuyerId, cancellationToken))
        {
            await SendOneAsync(order, NotificationKind.OrderDispatched, number, cancellationToken);
            await ScheduleFollowUpAsync(order, number, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Call off any not-yet-sent follow-up first, so a "how did delivery go?" message can never reach
        // the shopper for an order that was cancelled.
        await CancelPendingFollowUpsAsync(order, cancellationToken);

        foreach (var number in await GetOwnerNumbersAsync(order.BuyerId, cancellationToken))
        {
            await SendOneAsync(order, NotificationKind.OrderCancelled, number, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<string>> GetOwnerNumbersAsync(string ownerId, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
        return numbers.Select(n => n.PhoneNumber).ToList();
    }

    private async Task SendOneAsync(Order order, NotificationKind kind, string toNumber, CancellationToken ct)
    {
        var body = NotificationMessages.For(kind, order);
        var notification = new SmsNotification(order.BuyerId, order.Id, kind, toNumber, body);
        try
        {
            var result = await _gateway.SendAsync(toNumber, body, ct);
            notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, result.DateSent);
        }
        catch (SmsGatewayException ex)
        {
            notification.RecordProviderResult(null, "failed", ex.ProviderErrorCode, ex.Message, null);
            _logger.LogWarning($"Order #{order.Id}: {kind} message was refused by the provider (code {ex.ProviderErrorCode}).");
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure(PhoneNumberRedactor.Scrub(ex.Message));
            _logger.LogWarning($"Order #{order.Id}: {kind} message could not be handed to the provider: {PhoneNumberRedactor.Scrub(ex.Message)}");
        }

        await _notifications.AddAsync(notification, ct);
    }

    private async Task ScheduleFollowUpAsync(Order order, string toNumber, CancellationToken ct)
    {
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = NotificationMessages.For(NotificationKind.DeliveryFollowUp, order);
        var notification = new SmsNotification(order.BuyerId, order.Id, NotificationKind.DeliveryFollowUp, toNumber, body, scheduledFor: sendAt);
        try
        {
            var result = await _gateway.ScheduleAsync(toNumber, body, sendAt, ct);
            notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, result.DateSent);
        }
        catch (SmsGatewayException ex)
        {
            notification.RecordProviderResult(null, "failed", ex.ProviderErrorCode, ex.Message, null);
            _logger.LogWarning($"Order #{order.Id}: delivery follow-up could not be scheduled with the provider (code {ex.ProviderErrorCode}).");
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure(PhoneNumberRedactor.Scrub(ex.Message));
            _logger.LogWarning($"Order #{order.Id}: delivery follow-up could not be scheduled: {PhoneNumberRedactor.Scrub(ex.Message)}");
        }

        await _notifications.AddAsync(notification, ct);
    }

    private async Task CancelPendingFollowUpsAsync(Order order, CancellationToken ct)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpsByOrderSpecification(order.Id), ct);
        foreach (var followUp in pending)
        {
            try
            {
                await _gateway.CancelScheduledAsync(followUp.ProviderSid!, ct);
                followUp.UpdateDeliveryOutcome("canceled", null, null, null);
                await _notifications.UpdateAsync(followUp, ct);
                _logger.LogInformation($"Order #{order.Id}: scheduled delivery follow-up {followUp.ProviderSid} was called off.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Order #{order.Id}: could not call off scheduled follow-up {followUp.ProviderSid}: {PhoneNumberRedactor.Scrub(ex.Message)}");
            }
        }
    }
}
