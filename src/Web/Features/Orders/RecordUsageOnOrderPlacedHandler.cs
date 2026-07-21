using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Orders;

/// <summary>
/// UC2 automatic-usage hook (decided in plan.md §8): one order placed records one billable unit
/// against the buyer's active subscription's metered "api-call" component, if they have one.
/// Best-effort - never throws back into the MediatR publish call (which is itself best-effort
/// from <see cref="Microsoft.eShopWeb.ApplicationCore.Services.OrderService"/>).
/// </summary>
public class RecordUsageOnOrderPlacedHandler : INotificationHandler<OrderPlaced>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<RecordUsageOnOrderPlacedHandler> _logger;

    public RecordUsageOnOrderPlacedHandler(ISubscriptionService subscriptionService, IAppLogger<RecordUsageOnOrderPlacedHandler> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public async Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        try
        {
            var subscriptions = await _subscriptionService.ListMySubscriptionsAsync(notification.BuyerId, cancellationToken);
            var active = subscriptions.FirstOrDefault(s => s.State == BillingSubscriptionState.Active);
            if (active is null)
            {
                return;
            }

            await _subscriptionService.RecordUsageAsync(
                notification.BuyerId,
                active.Id,
                quantity: 1,
                memo: $"Order {notification.OrderId} placed",
                isAdmin: false,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to record automatic API-call usage for order {0}: {1}", notification.OrderId, ex.Message);
        }
    }
}
