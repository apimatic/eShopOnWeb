using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Bills one metered unit for every order placed (plan.md §8, UC2 trigger). Shoppers without a
/// subscription simply place orders as before, so a missing subscription is not an error here.
/// </summary>
public class RecordUsageOnOrderPlacedHandler : INotificationHandler<OrderPlaced>
{
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
            var report = await _subscriptionService.RecordUsageAsync(notification.BuyerId, 1,
                $"Order {notification.OrderId} placed", cancellationToken);

            _logger.LogInformation("Recorded 1 unit of usage for {Buyer} on order {OrderId}; period-to-date total is {Total}",
                notification.BuyerId, notification.OrderId, report.PeriodToDateBalance);
        }
        catch (NoActiveSubscriptionException)
        {
            _logger.LogInformation("{Buyer} has no active subscription, so order {OrderId} recorded no usage",
                notification.BuyerId, notification.OrderId);
        }
        catch (BillingProviderException ex)
        {
            // Checkout has already succeeded — a billing hiccup must not undo the customer's order.
            _logger.LogWarning("Could not record usage for order {OrderId}: {Message}",
                notification.OrderId, ex.Message);
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Could not record usage for order {OrderId}: {Message}",
                notification.OrderId, ex.Message);
        }
    }
}
