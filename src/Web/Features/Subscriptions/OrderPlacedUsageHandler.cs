using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

// UC2's demo hook: one order placed -> one billable "api-call" usage unit, for whichever
// active subscription (if any) the buyer holds. Buyers without an active subscription
// (including guest checkouts) are a no-op — this never blocks or fails order placement.
public class OrderPlacedUsageHandler : INotificationHandler<OrderPlaced>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<OrderPlacedUsageHandler> _logger;

    public OrderPlacedUsageHandler(ISubscriptionService subscriptionService, IAppLogger<OrderPlacedUsageHandler> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public async Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        try
        {
            var subscriptions = await _subscriptionService.GetMySubscriptionsAsync(notification.BuyerId, cancellationToken);
            var activeSubscription = subscriptions.FirstOrDefault(s =>
                s.State == SubscriptionStates.Active || s.State == SubscriptionStates.Trialing);

            if (activeSubscription == null)
            {
                return;
            }

            await _subscriptionService.RecordUsageAsync(notification.BuyerId, activeSubscription.Id, quantity: 1,
                memo: $"Order #{notification.OrderId}", ct: cancellationToken);

            _logger.LogInformation("Recorded 1 usage unit on subscription {0} for order {1}.",
                activeSubscription.Id, notification.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to record order-placed usage for order {0} (buyer {1}): {2}",
                notification.OrderId, notification.BuyerId, ex.Message);
        }
    }
}
