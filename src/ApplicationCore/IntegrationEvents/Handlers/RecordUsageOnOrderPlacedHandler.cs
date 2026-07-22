using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Turns "one order placed" into one billable unit of metered usage (plan.md §8, UC2).
/// <para>
/// This handler runs on the checkout path, so it is written to be incapable of harming it. Every
/// failure mode — the buyer having no subscription, the provider refusing, the provider being
/// unreachable, anything unforeseen — is caught and logged. An order is never rolled back, blocked
/// or delayed because usage could not be metered.
/// </para>
/// </summary>
public class RecordUsageOnOrderPlacedHandler : INotificationHandler<OrderPlaced>
{
    /// <summary>One order placed bills exactly one unit of the metered component.</summary>
    private const decimal UnitsPerOrder = 1m;

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
        if (string.IsNullOrWhiteSpace(notification.BuyerId))
        {
            return;
        }

        try
        {
            var memo = string.Format(CultureInfo.InvariantCulture, "eShopOnWeb order {0}", notification.OrderId);
            var summary = await _subscriptionService.RecordUsageAsync(notification.BuyerId, UnitsPerOrder, memo, cancellationToken);

            _logger.LogInformation("Order {0} recorded {1} unit of metered usage on subscription {2}.",
                notification.OrderId, UnitsPerOrder, summary.Recorded.SubscriptionId);
        }
        catch (NoActiveSubscriptionException)
        {
            // Buyers without a subscription are the norm on this storefront, not a problem.
        }
        catch (Exception ex)
        {
            // The order is already committed. Billing must never be allowed to undo or block it.
            _logger.LogWarning("Order {0} was placed successfully, but metered usage could not be recorded for {1}: {2}",
                notification.OrderId, notification.BuyerId, ex.Message);
        }
    }
}
