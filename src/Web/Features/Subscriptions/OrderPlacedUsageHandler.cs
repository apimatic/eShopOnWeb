using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// UC2's automatic usage hook (decided §8): one order placed records one usage unit against the
/// buyer's active subscription, if they have one. Best-effort and in-process only (§2.5) — a
/// failure here must never affect the order, which has already been created successfully.
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
            var subscriptions = await _subscriptionService.GetSubscriptionsForCustomerAsync(notification.BuyerId, cancellationToken);
            var activeSubscription = subscriptions.FirstOrDefault(s => s.State == SubscriptionLifecycleState.Active);
            if (activeSubscription is null)
            {
                return;
            }

            await _subscriptionService.RecordUsageAsync(
                notification.BuyerId,
                actingAsAdmin: false,
                activeSubscription.Id,
                quantity: 1,
                memo: $"eShopOnWeb order {notification.OrderId}",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Could not record automatic usage for order {0} (buyer {1}): {2}",
                notification.OrderId, notification.BuyerId, ex.Message);
        }
    }
}
