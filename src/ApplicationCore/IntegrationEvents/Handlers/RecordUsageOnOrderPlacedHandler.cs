using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// UC2's automatic trigger: one order placed bills one pay-as-you-go unit against the shopper's
/// live subscription.
/// </summary>
/// <remarks>
/// This handler is deliberately total — every failure path, including an unreachable billing
/// provider, is caught and logged. eShopOnWeb's order has already been persisted by the time
/// this runs, and nothing here is allowed to fail checkout or roll the order back.
/// </remarks>
public class RecordUsageOnOrderPlacedHandler : INotificationHandler<OrderPlaced>
{
    private const decimal UnitsPerOrder = 1m;

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
                UnitsPerOrder,
                $"eShopOnWeb order {notification.OrderId}",
                cancellationToken);

            if (report is null)
            {
                // No live subscription: an ordinary outcome for a shopper who has not subscribed.
                return;
            }

            _logger.LogInformation(
                "Recorded {0} usage unit for order {1} on subscription {2}; period to date: {3}.",
                UnitsPerOrder,
                notification.OrderId,
                report.Record.SubscriptionId,
                report.PeriodToDateQuantity?.ToString(CultureInfo.InvariantCulture) ?? "unavailable");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Could not record pay-as-you-go usage for order {0}; the order is unaffected. {1}",
                notification.OrderId,
                ex.Message);
        }
    }
}
