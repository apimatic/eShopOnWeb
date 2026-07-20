using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// UC2 automatic usage trigger: one order placed records one <c>api-call</c> unit against the
/// buyer's active subscription, if they have one. Best-effort — <see cref="Services.OrderService"/>
/// (ApplicationCore) already contains the try/catch that keeps a handler failure from rolling
/// back the order.
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
        var active = subscriptions.FirstOrDefault(s => s.State == SubscriptionLifecycleState.Active);
        if (active is null)
        {
            _logger.LogInformation("Order {0} placed by {1}; no active subscription to record usage against.", notification.OrderId, notification.BuyerId);
            return;
        }

        var result = await _subscriptionService.RecordUsageAsync(active.Id, 1m, $"order {notification.OrderId}", notification.BuyerId, cancellationToken);
        var balanceDisplay = result.PeriodToDateBalance?.ToString() ?? "unavailable";
        _logger.LogInformation("Order {0} recorded 1 api-call unit against subscription {1} (period-to-date balance: {2}).", notification.OrderId, active.Id, balanceDisplay);
    }
}
