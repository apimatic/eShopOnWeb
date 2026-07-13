using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// UC2 demo wiring: "one order placed -> one billable unit". Reacts to OrderPlaced by recording a
/// single usage unit against the buyer's active subscription, if they have one. Best-effort - a
/// failure here never affects the order that already succeeded (plan.md §2.5).
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
            var subscription = await _subscriptionService.GetActiveSubscriptionAsync(notification.BuyerId, cancellationToken);
            if (subscription == null)
            {
                return;
            }

            await _subscriptionService.RecordUsageAsync(subscription.Id, 1, $"Order {notification.OrderId} placed", cancellationToken);
            _logger.LogInformation("Recorded 1 usage unit on subscription {0} for order {1}", subscription.Id, notification.OrderId);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Failed to record usage for order {0}: {1}", notification.OrderId, ex.Message);
        }
        catch (InvalidSubscriptionStateException ex)
        {
            _logger.LogWarning("Skipped usage for order {0}: {1}", notification.OrderId, ex.Message);
        }
    }
}
