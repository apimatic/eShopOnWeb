using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Meters one billable unit per order placed (UC2's automatic trigger).
/// </summary>
/// <remarks>
/// This handler is deliberately total: a buyer without a subscription, a misconfigured component, or
/// an unreachable provider are all logged and swallowed. Metering is an additive concern and must
/// never fail, roll back, or block eShopOnWeb's order lifecycle.
/// </remarks>
public class RecordOrderUsageHandler : INotificationHandler<OrderPlaced>
{
    private const decimal UnitsPerOrder = 1m;

    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<RecordOrderUsageHandler> _logger;

    public RecordOrderUsageHandler(ISubscriptionService subscriptionService,
        IAppLogger<RecordOrderUsageHandler> logger)
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
            var report = await _subscriptionService.RecordUsageAsync(
                notification.BuyerId,
                UnitsPerOrder,
                $"eShopOnWeb order {notification.OrderId}",
                cancellationToken);

            _logger.LogInformation(
                $"Order {notification.OrderId} metered 1 unit against subscription " +
                $"{report.Record.SubscriptionId}; period to date " +
                (report.PeriodToDateUnitsAvailable ? $"{report.PeriodToDateUnits} units." : "unavailable."));
        }
        catch (SubscriptionNotFoundException)
        {
            // The overwhelmingly common case: the buyer simply has no subscription. Not noteworthy.
        }
        catch (Exception ex) when (ex is InvalidSubscriptionTransitionException
                                       or BillingConfigurationException
                                       or BillingProviderException)
        {
            _logger.LogWarning(
                $"Order {notification.OrderId} could not be metered for {notification.BuyerId}: {ex.Message}");
        }
    }
}
