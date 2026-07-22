using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// UC2's automatic trigger: one eShopOnWeb order placed records one billable unit against the buyer's
/// active subscription.
/// </summary>
/// <remarks>
/// This handler is deliberately total — it never propagates a failure. The order has already been
/// persisted by the time it runs, and a billing outage must not surface as a failed checkout or roll
/// anything back. A buyer with no active subscription (including every anonymous, cookie-identified
/// basket) is simply skipped.
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
        // Anonymous baskets are identified by a GUID cookie, not by an eShopOnWeb identity, so they can
        // never map to a billing customer.
        if (string.IsNullOrWhiteSpace(notification.BuyerId) || Guid.TryParse(notification.BuyerId, out _))
        {
            return;
        }

        try
        {
            var subscription = await _subscriptionService.GetActiveSubscriptionAsync(notification.BuyerId, cancellationToken);
            if (subscription is null)
            {
                return;
            }

            var summary = await _subscriptionService.RecordUsageForSubscriptionAsync(
                subscription.Id,
                UnitsPerOrder,
                $"eShopOnWeb order {notification.OrderId}",
                cancellationToken);

            _logger.LogInformation(
                "Recorded {0} billable unit(s) on subscription {1} for order {2}; period-to-date balance {3}.",
                UnitsPerOrder,
                subscription.Id,
                notification.OrderId,
                summary.PeriodToDateUnits?.ToString(CultureInfo.InvariantCulture) ?? "unavailable");
        }
        catch (Exception ex)
        {
            // Best-effort by design: the order stands regardless of what the billing provider does.
            _logger.LogWarning(
                "Could not record pay-as-you-go usage for order {0} (buyer {1}): {2}",
                notification.OrderId,
                notification.BuyerId,
                ex.Message);
        }
    }
}
