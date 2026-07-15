using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// UC2's "automatic" usage hook (plan.md §8): one order placed records one usage unit against the buyer's
/// active subscription, if they have one. A buyer with no active subscription is a no-op — this never
/// blocks or fails the checkout flow that raised <see cref="OrderPlaced"/>.
/// </summary>
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
            var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(notification.BuyerId, cancellationToken);
            var activeSubscription = subscriptions.FirstOrDefault(s => string.Equals(s.State, "active", StringComparison.OrdinalIgnoreCase));
            if (activeSubscription is null)
            {
                return;
            }

            await _subscriptionService.RecordUsageAsync(
                notification.BuyerId,
                actingAsAdmin: false,
                activeSubscription.Id,
                quantity: 1,
                memo: $"Order {notification.OrderId} placed",
                cancellationToken);
        }
        catch (SubscriptionNotFoundException)
        {
            // The subscription was cancelled/reassigned between the snapshot above and the record-usage
            // call below — a benign stale-read race, not a fault. Skip silently, same as "no subscription".
        }
        catch (InvalidSubscriptionTransitionException ex)
        {
            // The subscription stopped being active in that same window (e.g. a concurrent pause/cancel).
            // Skip — one missed usage unit never blocks or fails the checkout that raised this event.
            _logger.LogWarning("Skipped order-placed usage for buyer {0}: {1}", notification.BuyerId, ex.Message);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Could not record order-placed usage for buyer {0}: {1}", notification.BuyerId, ex.Message);
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Could not record order-placed usage for buyer {0} (configuration): {1}", notification.BuyerId, ex.Message);
        }
    }
}
