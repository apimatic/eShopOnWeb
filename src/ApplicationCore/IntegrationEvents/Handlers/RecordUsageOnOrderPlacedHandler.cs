using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// The automatic pay-as-you-go trigger: one order placed records one billable unit against the
/// buyer's active subscriptions (plan.md §8, UC2).
/// </summary>
/// <remarks>
/// This handler runs after the order has already been persisted. eShopOnWeb's order lifecycle must
/// never depend on the billing provider being reachable, so every failure — no subscription, a
/// misconfigured component, an unreachable provider — is logged and swallowed. Checkout succeeds
/// either way.
/// </remarks>
public class RecordUsageOnOrderPlacedHandler : INotificationHandler<OrderPlaced>
{
    private const int UnitsPerOrder = 1;

    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<RecordUsageOnOrderPlacedHandler> _logger;

    public RecordUsageOnOrderPlacedHandler(ISubscriptionService subscriptionService,
        IAppLogger<RecordUsageOnOrderPlacedHandler> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public async Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        try
        {
            var reports = await _subscriptionService.RecordUsageForUserAsync(
                notification.BuyerId,
                UnitsPerOrder,
                $"eShopOnWeb order {notification.OrderId}",
                cancellationToken);

            if (reports.Count == 0)
            {
                _logger.LogInformation(
                    "Order {0} placed by {1} recorded no usage: the buyer has no active subscription.",
                    notification.OrderId,
                    notification.BuyerId);
                return;
            }

            foreach (var report in reports)
            {
                _logger.LogInformation(
                    "Order {0} recorded {1} unit(s) of usage; period to date: {2}.",
                    notification.OrderId,
                    report.Recorded.Quantity,
                    report.IsTotalAvailable ? report.PeriodToDateQuantity!.Value.ToString() : "unavailable");
            }
        }
        catch (Exception ex)
        {
            // The order is already placed. Billing must never undo or block it (plan.md §2.5).
            _logger.LogWarning(
                "Could not record pay-as-you-go usage for order {0} placed by {1}: {2}",
                notification.OrderId,
                notification.BuyerId,
                ex.Message);
        }
    }
}
