using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// UC2 automatic trigger (§8): turns "one order placed" into one billable metered unit on the buyer's
/// active subscription. Best-effort — any failure (no subscription, provider down) is logged and
/// swallowed so it never breaks the checkout that raised the event.
/// </summary>
public class RecordOrderUsageHandler : INotificationHandler<OrderCreated>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<RecordOrderUsageHandler> _logger;

    public RecordOrderUsageHandler(ISubscriptionService subscriptionService,
        IAppLogger<RecordOrderUsageHandler> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public async Task Handle(OrderCreated notification, CancellationToken cancellationToken)
    {
        try
        {
            var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(notification.BuyerId, cancellationToken);
            var active = subscriptions.FirstOrDefault(s => s.IsActive);
            if (active is null)
            {
                _logger.LogInformation($"Order {notification.OrderId}: buyer {notification.BuyerId} has no active subscription; no usage recorded.");
                return;
            }

            var usage = await _subscriptionService.RecordUsageAsync(
                active.Id, 1, $"Order {notification.OrderId} placed", cancellationToken);
            _logger.LogInformation($"Order {notification.OrderId}: recorded 1 api-call unit on subscription {active.Id}; period-to-date total {usage.PeriodToDateTotal}.");
        }
        catch (Exception ex)
        {
            // Best-effort (§2.5): never fail the checkout because usage metering hiccuped.
            _logger.LogWarning($"Order {notification.OrderId}: failed to record usage for buyer {notification.BuyerId}: {ex.Message}");
        }
    }
}
