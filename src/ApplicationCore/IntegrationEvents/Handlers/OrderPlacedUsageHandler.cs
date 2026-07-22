using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Turns "one order placed" into one billable metered unit on the buyer's active subscription
/// (UC2's automatic trigger).
/// </summary>
/// <remarks>
/// This handler is deliberately total: a buyer with no subscription, a misconfigured component, or
/// an unreachable billing provider must all leave the completed order untouched. Every failure is
/// logged and swallowed here so nothing can escape into eShopOnWeb's checkout path.
/// </remarks>
public class OrderPlacedUsageHandler : INotificationHandler<OrderPlaced>
{
    private const decimal UnitsPerOrder = 1m;

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
            var receipt = await _subscriptionService.RecordUsageForUserAsync(
                notification.BuyerId,
                UnitsPerOrder,
                $"eShopOnWeb order {notification.OrderId}",
                cancellationToken);

            if (receipt is null)
            {
                _logger.LogInformation(
                    "Order {OrderId} placed by {BuyerId} recorded no metered usage: the buyer has no active subscription.",
                    notification.OrderId, notification.BuyerId);
                return;
            }

            _logger.LogInformation(
                "Order {OrderId} recorded {Units} metered unit(s) on subscription {SubscriptionId}; period-to-date total is {Total}.",
                notification.OrderId,
                UnitsPerOrder,
                receipt.Recorded.SubscriptionId,
                receipt.PeriodToDateUnits?.ToString() ?? "unavailable");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Order {OrderId} was placed successfully but metered usage could not be recorded for {BuyerId}: {Message}",
                notification.OrderId, notification.BuyerId, ex.Message);
        }
    }
}
