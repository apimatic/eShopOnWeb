using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// The automatic UC2 trigger decided in §8: one order placed bills one unit of pay-as-you-go usage
/// against the buyer's active subscription. Shoppers without a subscription simply record nothing,
/// and a billing failure never fails the checkout that already completed.
/// </summary>
public class OrderPlacedUsageHandler : INotificationHandler<OrderPlaced>
{
    private const decimal UnitsPerOrder = 1;

    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<OrderPlacedUsageHandler> _logger;

    public OrderPlacedUsageHandler(ISubscriptionService subscriptionService,
        IAppLogger<OrderPlacedUsageHandler> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public async Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        try
        {
            var report = await _subscriptionService.RecordUsageForUserAsync(notification.BuyerId, UnitsPerOrder,
                $"Order {notification.Order.Id}", cancellationToken);

            if (report is not null)
            {
                _logger.LogInformation("Order {0} recorded {1} unit of usage; period-to-date total is {2}.",
                    notification.Order.Id, UnitsPerOrder,
                    report.IsSummaryAvailable ? report.Summary!.UnitBalance.ToString() : "unavailable");
            }
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionOperationException)
        {
            // The order stands; usage billing is additive and must not fail checkout.
            _logger.LogWarning("Could not record usage for order {0}: {1}", notification.Order.Id, ex.Message);
        }
    }
}
