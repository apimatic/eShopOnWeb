using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// UC2's "one order placed → one billable unit" hook (§8 decision): reacts to
/// <see cref="OrderPlaced"/> by recording one api-call usage unit against the buyer's active
/// subscription, if any. Best-effort — <see cref="ISubscriptionService.RecordOrderPlacedUsageAsync"/>
/// never throws, so a Maxio failure here can never block or roll back order placement.
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
