using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// UC2's automatic trigger: one order placed records one billable unit against the buyer's
/// subscription.
/// </summary>
/// <remarks>
/// This handler is deliberately total — a buyer without a subscription, a provider outage, or any
/// other billing failure is logged and swallowed. Recording usage is additive to eShopOnWeb's order
/// lifecycle and must never fail or roll back a checkout.
/// </remarks>
public class OrderPlacedUsageHandler : INotificationHandler<OrderPlaced>
{
    private const decimal UnitsPerOrder = 1m;

    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<OrderPlacedUsageHandler> _logger;

    public OrderPlacedUsageHandler(ISubscriptionService subscriptionService,
        IAppLogger<OrderPlacedUsageHandler> logger)
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
                UnitsPerOrder,
                $"eShopOnWeb order {notification.OrderId}",
                cancellationToken);

            var periodToDate = report.PeriodToDateUnits?.ToString(CultureInfo.InvariantCulture) ?? "unavailable";

            _logger.LogInformation(
                "Recorded {0} unit(s) of metered usage for order {1} on subscription {2}; period-to-date {3}.",
                UnitsPerOrder,
                notification.OrderId,
                report.Record.SubscriptionId,
                periodToDate);
        }
        catch (Exception ex)
        {
            // Never let a billing problem affect the order that was just placed.
            _logger.LogWarning(
                "Could not record metered usage for order {0} (buyer {1}): {2}",
                notification.OrderId,
                notification.BuyerId,
                ex.Message);
        }
    }
}
