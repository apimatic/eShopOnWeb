using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Records a completed lifecycle transition for audit. Runs in-process off the
/// <see cref="SubscriptionStateChanged"/> notification (UC4, step 3).
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
            "Subscription {SubscriptionId} transitioned {PreviousState} -> {NewState} via {Action}. Reason: {Reason}",
            notification.Subscription.Id,
            notification.PreviousState,
            notification.NewState,
            notification.Action,
            notification.Reason ?? "(none given)");

        return Task.CompletedTask;
    }
}
