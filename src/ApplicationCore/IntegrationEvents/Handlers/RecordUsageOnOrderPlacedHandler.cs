using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// One order placed bills one metered unit (UC2). Checkout must never fail because the billing
/// provider was unhappy, so a failure here is logged and swallowed.
/// </summary>
public class RecordUsageOnOrderPlacedHandler : INotificationHandler<OrderPlaced>
{
    private static readonly string[] LiveStates = { "active", "trialing", "assessing", "past_due" };

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
            var subscriptions = await _subscriptionService.ListSubscriptionsAsync(notification.BuyerId, cancellationToken);
            var active = subscriptions.FirstOrDefault(s => LiveStates.Contains(s.State));
            if (active is null)
            {
                return;
            }

            await _subscriptionService.RecordUsageAsync(
                active.BillingSubscriptionId, 1, $"Order {notification.OrderId}", cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning($"Order {notification.OrderId} could not be metered against the customer's subscription: {ex.Message}");
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning($"Order {notification.OrderId} could not be metered: {ex.Message}");
        }
    }
}
