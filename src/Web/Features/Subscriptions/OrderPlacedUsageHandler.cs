using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// UC2's "automatic" usage hook (plan.md §8): one order placed records one billable unit
/// against the buyer's active subscription, if they have one. Best-effort — RecordOrderPlacedUsageAsync
/// itself swallows billing-provider failures so a Maxio outage never blocks checkout.
/// </summary>
public class OrderPlacedUsageHandler : INotificationHandler<OrderPlaced>
{
    private readonly ISubscriptionService _subscriptionService;

    public OrderPlacedUsageHandler(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken) =>
        _subscriptionService.RecordOrderPlacedUsageAsync(notification.BuyerId, cancellationToken);
}
