using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Meters one billable unit per order placed (UC2). Shoppers without an active subscription simply are
/// not metered.
/// </summary>
/// <remarks>
/// This handler never propagates a failure. The order is already committed by the time it runs, and no
/// billing problem — a missing subscription, a misconfigured component, an unreachable provider — may be
/// allowed to affect eShopOnWeb's order lifecycle.
/// </remarks>
public class OrderPlacedUsageHandler : INotificationHandler<OrderPlaced>
{
    private const decimal UNITS_PER_ORDER = 1m;

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
            var report = await _subscriptionService.RecordUsageForUserAsync(
                notification.BuyerId,
                UNITS_PER_ORDER,
                $"eShopOnWeb order {notification.OrderId}",
                cancellationToken);

            _logger.LogInformation("Metered 1 unit against subscription {SubscriptionId} for order {OrderId}.",
                report.SubscriptionId, notification.OrderId);
        }
        catch (InvalidSubscriptionOperationException)
        {
            // No active subscription for this shopper: metering simply does not apply to their order.
            _logger.LogInformation("Order {OrderId} was not metered because {BuyerId} has no active subscription.",
                notification.OrderId, notification.BuyerId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId} could not be metered against {BuyerId}: {Reason}",
                notification.OrderId, notification.BuyerId, ex.Message);
        }
    }
}
