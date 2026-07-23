using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// UC2's automatic trigger: one order placed records one billable API call against the buyer's
/// subscription. A buyer with no subscription, or a provider failure, is logged and ignored — the
/// order has already been placed and must never be rolled back for a metering side effect.
/// </summary>
public class RecordUsageOnOrderPlacedHandler : INotificationHandler<OrderPlaced>
{
    private const decimal UnitsPerOrder = 1;

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
            var report = await _subscriptionService.RecordUsageAsync(notification.UserName,
                UnitsPerOrder,
                $"Order {notification.Order.Id}",
                cancellationToken);

            _logger.LogInformation(
                $"Recorded {UnitsPerOrder} unit of usage for order {notification.Order.Id} on subscription {report.Record.SubscriptionId}.");
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionOperationException)
        {
            _logger.LogWarning(
                $"Order {notification.Order.Id} placed by {notification.UserName} did not record usage: {ex.Message}");
        }
    }
}
