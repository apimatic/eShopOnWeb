using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// UC2's automatic usage trigger (plan §8 decisions): one order placed records one <c>api-call</c>
/// usage unit against the buyer's active subscription, if they have one. Silently does nothing for
/// buyers without a subscription — this is an opt-in demo behavior, not a requirement to subscribe.
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
        var subscriptions = await _subscriptionService.GetMySubscriptionsAsync(notification.BuyerId, cancellationToken);
        var activeSubscription = subscriptions.FirstOrDefault(s =>
            s.State is BillingSubscriptionState.Active or BillingSubscriptionState.Trialing);

        if (activeSubscription is null)
        {
            return;
        }

        await _subscriptionService.RecordUsageAsync(
            notification.BuyerId,
            activeSubscription.Id,
            quantity: 1,
            memo: $"Order #{notification.OrderId}",
            isAdmin: false,
            cancellationToken);

        _logger.LogInformation("Recorded 1 api-call usage unit against subscription {SubscriptionId} for order {OrderId}.", activeSubscription.Id, notification.OrderId);
    }
}
