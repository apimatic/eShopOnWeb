using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// UC2's automatic trigger: one order placed records one billable unit against the buyer's
/// subscription. Buyers without an active subscription are simply skipped, and a metering
/// failure never fails the checkout that already succeeded.
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
            var report = await _subscriptionService.RecordUsageForUserAsync(notification.BuyerId, 1,
                "eShopOnWeb order placed", cancellationToken);

            if (report is null)
            {
                _logger.LogInformation("{0} placed an order but holds no active subscription; no usage recorded.",
                    notification.BuyerId);

                return;
            }

            _logger.LogInformation("Recorded 1 unit for {0}; period-to-date total is now {1}.",
                notification.BuyerId, report.PeriodToDateTotal);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not record pay-as-you-go usage for {0}: {1}",
                notification.BuyerId, ex.Message);
        }
    }
}
