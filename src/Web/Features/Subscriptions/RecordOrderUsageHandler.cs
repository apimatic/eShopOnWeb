using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Turns one placed eShopOnWeb order into one billable unit of metered usage (plan.md §8, UC2).
/// </summary>
/// <remarks>
/// Every failure path here is swallowed. Checkout has already completed by the time this runs, and a
/// billing outage — or a shopper who simply has no subscription — must never turn a successful order into
/// an error for the customer.
/// </remarks>
public class RecordOrderUsageHandler : INotificationHandler<OrderPlaced>
{
    private const int UnitsPerOrder = 1;

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
            var summary = await _subscriptionService.RecordUsageForUserAsync(
                notification.BuyerId,
                UnitsPerOrder,
                $"eShopOnWeb order #{notification.OrderId}",
                cancellationToken);

            if (summary is null)
            {
                return;
            }

            var total = summary.PeriodToDateQuantity?.ToString() ?? "unavailable";
            _logger.LogInformation(
                "Order {0} recorded {1} unit of '{2}' usage for {3}; period-to-date total is now {4}.",
                notification.OrderId, UnitsPerOrder, summary.ComponentHandle, notification.BuyerId, total);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {0} could not be metered for {1}: {2}",
                notification.OrderId, notification.BuyerId, ex.Message);
        }
    }
}
