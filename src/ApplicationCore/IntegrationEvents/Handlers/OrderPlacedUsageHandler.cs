using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// UC2's "automatic usage" demo hook (plan §8): one order placed records one billable unit against the
/// buyer's active subscription, if they have one. This is a best-effort side effect of checkout — any
/// failure (no subscription, provider unreachable, etc.) is logged and swallowed here, never rethrown, so
/// a Maxio problem can never roll back or block an eShopOnWeb order.
/// </summary>
public class OrderPlacedUsageHandler : INotificationHandler<OrderPlaced>
{
    private const int UnitsPerOrder = 1;

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
            var active = subscriptions.FirstOrDefault(s => s.State is "active" or "trialing");
            if (active is null)
            {
                return;
            }

            await _subscriptionService.RecordUsageAsync(notification.BuyerId, active.Id, UnitsPerOrder,
                $"Order #{notification.OrderId}", isAdmin: false, cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning("Failed to record automatic usage for order {OrderId} placed by {BuyerId}: {Message}",
                notification.OrderId, notification.BuyerId, ex.Message);
        }
    }
}
