using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Bills one pay-as-you-go unit for every order placed (UC2's automatic trigger, §8).
/// </summary>
/// <remarks>
/// Most shoppers have no subscription, so "no active subscription" is the ordinary case and is
/// skipped quietly. Every failure is swallowed: this runs after the order is already persisted, and
/// a billing problem must never surface as a failed checkout.
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
            var result = await _subscriptionService.RecordUsageAsync(
                notification.BuyerId,
                UnitsPerOrder,
                $"eShopOnWeb order {notification.OrderId}",
                cancellationToken);

            _logger.LogInformation(
                "Recorded {0} unit of '{1}' for order {2} on subscription {3}; period-to-date total is {4}.",
                UnitsPerOrder,
                result.ComponentHandle,
                notification.OrderId,
                result.SubscriptionId,
                result.PeriodToDateAvailable ? result.PeriodToDateUnits! : "unavailable");
        }
        catch (InvalidSubscriptionOperationException)
        {
            // The shopper simply has no active subscription — nothing to meter.
            _logger.LogInformation(
                "Order {0} placed by {1} was not metered: the shopper has no active subscription.",
                notification.OrderId, notification.BuyerId);
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            _logger.LogWarning(
                "Could not meter order {0} for {1}: {2}. The order is unaffected.",
                notification.OrderId, notification.BuyerId, ex.Message);
        }
    }
}
