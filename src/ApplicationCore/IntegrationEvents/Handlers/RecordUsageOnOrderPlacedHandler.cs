using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// The automatic pay-as-you-go hook: one order placed records one billable unit against the buyer's
/// active subscription (plan.md §8, UC2 trigger).
/// </summary>
/// <remarks>
/// Every failure is swallowed and logged. A buyer with no subscription is the normal case, and a billing
/// outage must never roll back or block eShopOnWeb's order lifecycle.
/// </remarks>
public class RecordUsageOnOrderPlacedHandler : INotificationHandler<OrderPlaced>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<RecordUsageOnOrderPlacedHandler> _logger;

    public RecordUsageOnOrderPlacedHandler(
        ISubscriptionService subscriptionService,
        IAppLogger<RecordUsageOnOrderPlacedHandler> logger)
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
                SubscriptionConstants.UsageUnitsPerOrder,
                $"eShopOnWeb order {notification.OrderId}",
                cancellationToken);

            if (report is null)
            {
                _logger.LogInformation(
                    "Order {0}: buyer {1} has no active subscription, so no usage was recorded.",
                    notification.OrderId, notification.BuyerId);
                return;
            }

            _logger.LogInformation(
                "Order {0}: recorded {1} unit(s) of {2} against subscription {3}; period-to-date {4}.",
                notification.OrderId,
                SubscriptionConstants.UsageUnitsPerOrder,
                report.ComponentHandle,
                report.SubscriptionId,
                report.PeriodToDateUnits?.ToString("N0") ?? "unavailable");
        }
        catch (Exception ex)
        {
            // Never let a billing failure affect the order that has already been placed.
            _logger.LogWarning(
                "Order {0}: usage could not be recorded for buyer {1}: {2}",
                notification.OrderId, notification.BuyerId, ex.Message);
        }
    }
}
