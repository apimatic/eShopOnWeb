using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>UC2's automatic hook: one order placed records one api-call usage unit (plan.md §8).</summary>
public class OrderPlacedUsageHandler : INotificationHandler<OrderPlaced>
{
    private readonly ISubscriptionService _subscriptionService;

    public OrderPlacedUsageHandler(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        return _subscriptionService.RecordOrderPlacedUsageAsync(notification.BuyerId, cancellationToken);
    }
}
