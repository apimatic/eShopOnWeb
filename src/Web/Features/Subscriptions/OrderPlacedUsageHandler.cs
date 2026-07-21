using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// UC2's "one order placed -> one billable unit" demo hook: records one usage unit against the
/// buyer's active subscription, if they have one. Never throws — a missing/inactive subscription
/// (including eShopOnWeb's anonymous-basket buyers, who have no billing-provider customer at all)
/// is the common case, not an error, and a billing failure here must never affect order placement.
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
            var subscription = await _subscriptionService.FindActiveSubscriptionAsync(notification.BuyerId, cancellationToken);
            if (subscription == null)
            {
                return;
            }

            await _subscriptionService.RecordUsageAsync(subscription.Id, quantity: 1, memo: $"Order {notification.OrderId} placed", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to record usage for order {OrderId} (buyer {BuyerId}): {Message}", notification.OrderId, notification.BuyerId, ex.Message);
        }
    }
}
