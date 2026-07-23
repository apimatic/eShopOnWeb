using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Turns "one order placed" into "one billable unit" on the buyer's active subscription (UC2).
/// </summary>
/// <remarks>
/// Shoppers without a subscription are the normal case, and the billing provider may be unreachable
/// at any time, so this handler never lets a failure escape: the order has already been placed and
/// must not be affected.
/// </remarks>
public class RecordUsageOnOrderCreatedHandler : INotificationHandler<OrderCreated>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly SubscriptionSettings _settings;
    private readonly IAppLogger<RecordUsageOnOrderCreatedHandler> _logger;

    public RecordUsageOnOrderCreatedHandler(ISubscriptionService subscriptionService,
        IOptions<SubscriptionSettings> settings,
        IAppLogger<RecordUsageOnOrderCreatedHandler> logger)
    {
        _subscriptionService = subscriptionService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task Handle(OrderCreated notification, CancellationToken cancellationToken)
    {
        if (!_settings.RecordUsageOnOrderPlaced)
        {
            return;
        }

        try
        {
            var report = await _subscriptionService.TryRecordUsageForUserAsync(notification.BuyerId,
                quantity: 1m,
                memo: $"eShopOnWeb order {notification.OrderId}",
                cancellationToken);

            if (report is null)
            {
                return;
            }

            _logger.LogInformation(
                "Order {0} recorded one metered unit against subscription {1} for {2}.",
                notification.OrderId, report.SubscriptionId, notification.BuyerId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Order {0} was placed but usage could not be recorded for {1}; the order is unaffected. Error: {2}",
                notification.OrderId, notification.BuyerId, ex.Message);
        }
    }
}
