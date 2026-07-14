using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// UC2's automatic usage hook: one order placed records one unit of the metered component
/// against the buyer's active subscription, if they have one. Best-effort — never throws, so an
/// order is never blocked or rolled back by a billing-provider hiccup (§2.5).
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
            await _subscriptionService.RecordUsageForOrderAsync(notification.BuyerId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to record automatic usage for order placed by {0}: {1}", notification.BuyerId, ex.Message);
        }
    }
}
