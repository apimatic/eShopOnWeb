using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// One order placed bills one metered unit against the buyer's subscription.
/// Billing is strictly additive to checkout: a buyer without a subscription is skipped, and any billing
/// failure is logged and swallowed so the order lifecycle is never blocked or rolled back.
/// </summary>
public class RecordUsageOnOrderPlaced : INotificationHandler<OrderPlaced>
{
    private const decimal UnitsPerOrder = 1m;

    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<RecordUsageOnOrderPlaced> _logger;

    public RecordUsageOnOrderPlaced(ISubscriptionService subscriptionService,
        IAppLogger<RecordUsageOnOrderPlaced> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public async Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        try
        {
            var subscriptions = await _subscriptionService.GetMySubscriptionsAsync(notification.BuyerId,
                cancellationToken);

            var active = subscriptions.FirstOrDefault(s =>
                s.State == BillingSubscriptionState.Active || s.State == BillingSubscriptionState.Trialing);

            if (active is null)
            {
                return;
            }

            await _subscriptionService.RecordUsageAsync(notification.BuyerId, active.Id, UnitsPerOrder,
                $"Order {notification.OrderId}", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                $"Could not meter order {notification.OrderId} against a subscription; the order is unaffected. {ex.Message}");
        }
    }
}
