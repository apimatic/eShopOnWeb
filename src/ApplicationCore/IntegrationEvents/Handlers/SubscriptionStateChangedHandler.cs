using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// In-process reaction to UC4: audit the lifecycle transition that was applied.
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
            "Subscription {0} transitioned {1} -> {2} via {3}.",
            notification.Subscription.Id,
            notification.PreviousState,
            notification.NewState,
            notification.Action);

        return Task.CompletedTask;
    }
}
