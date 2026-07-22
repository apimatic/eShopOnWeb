using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Records a lifecycle transition in the application log.
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
        _logger.LogInformation("Subscription {SubscriptionId} {Action}: {PreviousStatus} -> {NewStatus}.",
            notification.Subscription.Id,
            notification.Action,
            notification.PreviousStatus,
            notification.NewStatus);

        return Task.CompletedTask;
    }
}
