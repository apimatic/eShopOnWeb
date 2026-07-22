using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// UC2's automatic trigger: one order placed meters one billable unit against the buyer's live
/// subscription (plan.md §8). Buyers without a subscription simply record nothing, and a metering
/// failure never fails the order — the order has already been committed.
/// </summary>
public class RecordOrderUsageHandler : INotificationHandler<OrderPlaced>
{
    private const decimal UnitsPerOrder = 1m;

    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<RecordOrderUsageHandler> _logger;

    public RecordOrderUsageHandler(ISubscriptionService subscriptionService,
        IAppLogger<RecordOrderUsageHandler> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public async Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        try
        {
            var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(notification.BuyerId, cancellationToken);
            var live = subscriptions.FirstOrDefault(s => s.IsLive);
            if (live is null)
            {
                return;
            }

            await _subscriptionService.RecordUsageAsync(live.Id, UnitsPerOrder,
                $"Order {notification.OrderId}", cancellationToken);
        }
        catch (System.Exception ex)
        {
            // Best-effort, exactly like the rest of the in-process eventing (plan.md §2.5).
            _logger.LogWarning("Could not meter order {0} for {1}: {2}",
                notification.OrderId, notification.BuyerId, ex.Message);
        }
    }
}
