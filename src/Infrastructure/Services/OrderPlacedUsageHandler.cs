using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Infrastructure.Services;

// Demo hook for "one order placed -> one billable unit" (plan §8/UC2): records one api-call
// usage unit against the buyer's active subscription, if they have one. Best-effort: billing
// hiccups must never fail checkout, so failures are logged and swallowed here.
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
            var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(notification.BuyerId, cancellationToken);
            var active = subscriptions.FirstOrDefault(s => s.State is "active" or "trialing");
            if (active is null)
            {
                return;
            }

            await _subscriptionService.RecordUsageAsync(notification.BuyerId, isAdmin: false, active.Id, quantity: 1,
                memo: $"Order {notification.OrderId} placed", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not record automatic usage for order {0} (buyer {1}): {2}", notification.OrderId, notification.BuyerId, ex.Message);
        }
    }
}
