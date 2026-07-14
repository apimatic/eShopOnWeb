using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// UC2's "one order placed → one billable unit" demo hook: records one usage unit against the
/// buyer's active subscription, if they have one. Billing is best-effort here — an order having
/// been placed must never be affected by a billing-provider failure, so any exception is caught
/// and logged rather than propagated.
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
            var subscription = await _subscriptionService.FindSubscriptionForUserAsync(notification.BuyerId, cancellationToken);
            if (subscription is null)
            {
                return;
            }

            await _subscriptionService.RecordUsageAsync(
                subscription.Id,
                quantity: 1,
                memo: $"Order {notification.OrderId} placed",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to record order-placed usage for {BuyerId} (order {OrderId}): {Message}",
                notification.BuyerId, notification.OrderId, ex.Message);
        }
    }
}
