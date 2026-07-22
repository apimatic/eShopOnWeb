using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// UC2's automatic trigger: one order placed records one metered unit against the buyer's active
/// subscription. Buyers without an active subscription are simply skipped, and a billing failure
/// never fails the order the customer has already placed.
/// </summary>
public class RecordUsageOnOrderPlacedHandler : INotificationHandler<OrderPlaced>
{
    private const decimal UnitsPerOrder = 1m;

    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<RecordUsageOnOrderPlacedHandler> _logger;

    public RecordUsageOnOrderPlacedHandler(ISubscriptionService subscriptionService,
        IAppLogger<RecordUsageOnOrderPlacedHandler> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public async Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        try
        {
            var subscriptions = await _subscriptionService.ListSubscriptionsAsync(notification.BuyerId, cancellationToken);
            var active = subscriptions.FirstOrDefault(subscription => SubscriptionStates.IsLive(subscription.State));
            if (active is null)
            {
                return;
            }

            await _subscriptionService.RecordUsageAsync(notification.BuyerId,
                active.BillingSubscriptionId,
                UnitsPerOrder,
                "Order placed on eShopOnWeb",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not record order usage for {notification.BuyerId}: {ex.Message}");
        }
    }
}
