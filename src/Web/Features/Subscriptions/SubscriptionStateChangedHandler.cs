using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Records a lifecycle transition in the application log (§2.5).
/// </summary>
public class SubscriptionStateChangedHandler : INotificationHandler<SubscriptionStateChanged>
{
    private readonly IAppLogger<SubscriptionStateChangedHandler> _logger;

    public SubscriptionStateChangedHandler(IAppLogger<SubscriptionStateChangedHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {0} for {1}: {2} moved it from {3} to {4}, effective {5}.",
            notification.SubscriptionId,
            notification.BuyerId,
            notification.Action,
            notification.PreviousState,
            notification.NewState,
            notification.EffectiveAt?.ToString("u") ?? "immediately");

        return Task.CompletedTask;
    }
}
