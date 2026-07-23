using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// UC2's automatic trigger: one order placed bills one unit of the metered component against the
/// buyer's subscription. Shoppers without a subscription simply have nothing to bill, and a billing
/// failure must never fail a checkout that has already succeeded.
/// </summary>
public class RecordOrderUsageHandler : INotificationHandler<OrderPlaced>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<RecordOrderUsageHandler> _logger;

    private const int UNITS_PER_ORDER = 1;

    public RecordOrderUsageHandler(ISubscriptionService subscriptionService,
        IAppLogger<RecordOrderUsageHandler> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public async Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        try
        {
            var report = await _subscriptionService.RecordUsageAsync(notification.BuyerId, UNITS_PER_ORDER,
                $"Order {notification.OrderId}", cancellationToken);

            _logger.LogInformation("Recorded {0} unit for order {1}; period-to-date total is {2}.",
                UNITS_PER_ORDER, notification.OrderId, report.PeriodToDateTotal?.ToString() ?? "unavailable");
        }
        catch (NoActiveSubscriptionException)
        {
            // Expected for shoppers who have never subscribed — there is nothing to meter.
        }
        catch (Exception exception) when (exception is BillingProviderException or BillingConfigurationException)
        {
            _logger.LogWarning("Could not record usage for order {0}: {1}", notification.OrderId, exception.Message);
        }
    }
}
