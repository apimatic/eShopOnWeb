using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// UC2's demo hook (plan.md §8 decision, UC2 "eShopOnWeb hook for automatic usage"): one order placed
/// records one billable unit against the buyer's active subscription, if they have one. A buyer with
/// no subscription is a normal, expected case — nothing is billed. Any billing-provider failure is
/// logged and swallowed here: an order that already succeeded must never be affected by Maxio being
/// unavailable.
/// </summary>
public class OrderPlacedHandler : INotificationHandler<OrderPlaced>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<OrderPlacedHandler> _logger;

    public OrderPlacedHandler(ISubscriptionService subscriptionService, IAppLogger<OrderPlacedHandler> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public async Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        try
        {
            var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(notification.BuyerId, cancellationToken);
            var activeSubscription = subscriptions.FirstOrDefault(s => s.IsActiveOrTrialing);
            if (activeSubscription is null)
            {
                return;
            }

            await _subscriptionService.RecordUsageAsync(
                activeSubscription.Id,
                ownerUserId: notification.BuyerId,
                quantity: 1,
                memo: $"Order {notification.OrderId} placed",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to record usage for order {OrderId} (buyer {BuyerId}): {Message}",
                notification.OrderId, notification.BuyerId, ex.Message);
        }
    }
}
