using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// UC2's "automatic" usage hook (§8 decision): one order placed records one pay-as-you-go unit
/// against the buyer's active subscription, if they have one. A billing failure here must never
/// affect the order that already committed, so every failure is caught and logged, never thrown.
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
            var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(notification.BuyerId, cancellationToken);
            var activeSubscription = subscriptions.FirstOrDefault(s => s.Status.IsActiveLike());
            if (activeSubscription is null)
            {
                return;
            }

            await _subscriptionService.RecordUsageAsync(
                activeSubscription.Id,
                quantity: 1,
                memo: $"Order #{notification.OrderId} placed",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "OrderPlaced usage hook failed for buyer {0}, order {1}: {2}",
                notification.BuyerId, notification.OrderId, ex.Message);
        }
    }
}
