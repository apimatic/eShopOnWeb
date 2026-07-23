using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// The pay-as-you-go hook: one order placed records one billable unit of metered usage against the
/// buyer's active subscription, which is billed on their next renewal invoice.
/// </summary>
/// <remarks>
/// Every failure path is contained here. A buyer with no subscription, a misconfigured component,
/// or an unreachable provider must never fail or roll back an order that eShopOnWeb has already
/// committed — so this handler logs and returns rather than propagating.
/// </remarks>
public class RecordUsageOnOrderPlacedHandler : INotificationHandler<OrderPlaced>
{
    /// <summary>One order placed bills one unit of the metered component.</summary>
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
            var subscription = await _subscriptionService.GetActiveSubscriptionAsync(
                notification.BuyerId, cancellationToken);

            if (subscription is null)
            {
                // Anonymous / cookie buyers and customers without a plan simply do not accrue usage.
                _logger.LogInformation(
                    "Order {OrderId}: buyer {BuyerId} has no active subscription, so no usage was recorded.",
                    notification.OrderId,
                    notification.BuyerId);
                return;
            }

            var result = await _subscriptionService.RecordUsageAsync(
                subscription.Id,
                UnitsPerOrder,
                $"eShopOnWeb order {notification.OrderId}",
                cancellationToken);

            _logger.LogInformation(
                "Order {OrderId}: recorded {Quantity} unit against subscription {SubscriptionId}; period-to-date balance is {Balance}.",
                notification.OrderId,
                result.Quantity,
                subscription.Id,
                result.PeriodToDateUnits?.ToString() ?? "unavailable");
        }
        catch (Exception ex) when (
            ex is BillingProviderException
              or BillingConfigurationException
              or InvalidSubscriptionOperationException
              or HttpRequestException
              or TaskCanceledException)
        {
            // Contained on purpose: the order stands regardless of what billing does.
            _logger.LogWarning(
                "Order {OrderId}: usage could not be recorded for buyer {BuyerId} ({Message}). The order is unaffected.",
                notification.OrderId,
                notification.BuyerId,
                ex.Message);
        }
    }
}
