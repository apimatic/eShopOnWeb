using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// UC2's automatic trigger: one order placed records one billable unit against the buyer's live
/// subscription (plan §8).
/// </summary>
/// <remarks>
/// Billing is strictly additive to eShopOnWeb's order lifecycle. A buyer without a live subscription
/// is not an error, and no billing failure may escape this handler — the order stands either way.
/// </remarks>
public class RecordOrderUsageHandler : INotificationHandler<OrderPlaced>
{
    private const decimal OneBillableUnit = 1m;

    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<RecordOrderUsageHandler> _logger;

    public RecordOrderUsageHandler(ISubscriptionService subscriptionService, IAppLogger<RecordOrderUsageHandler> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public async Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.BuyerId))
        {
            return;
        }

        var actor = new SubscriptionActor(notification.BuyerId, IsAdministrator: false);

        try
        {
            var subscriptions = await _subscriptionService.ListMySubscriptionsAsync(actor, cancellationToken);
            var subscription = subscriptions.FirstOrDefault(s => s.State == BillingSubscriptionState.Active)
                ?? subscriptions.FirstOrDefault(s => s.IsLive);

            if (subscription is null)
            {
                _logger.LogInformation(
                    "Order {OrderId} placed by {BuyerId} records no usage: the buyer holds no live subscription.",
                    notification.OrderId, notification.BuyerId);
                return;
            }

            await _subscriptionService.RecordUsageAsync(actor, subscription.Id, OneBillableUnit,
                $"eShopOnWeb order {notification.OrderId}", cancellationToken);
        }
        catch (Exception ex)
        {
            // Usage metering is additive. A billing outage must never fail an order that has already
            // been written, so the failure is recorded and swallowed here.
            _logger.LogWarning(
                "Order {OrderId} placed by {BuyerId} could not be metered: {Reason}",
                notification.OrderId, notification.BuyerId, ex.Message);
        }
    }
}
