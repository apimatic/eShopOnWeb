using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// UC2's automatic usage hook (§8): one eShopOnWeb order placed records one "api-call" usage unit
/// against the buyer's active subscription, if any. A buyer with no subscription is not an error.
/// </summary>
public class RecordOrderUsageHandler : INotificationHandler<OrderPlaced>
{
    private readonly ISubscriptionService _subscriptionService;

    public RecordOrderUsageHandler(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        return _subscriptionService.RecordAutomaticUsageAsync(
            notification.BuyerId, 1, $"eShopOnWeb order #{notification.OrderId} placed", cancellationToken);
    }
}
