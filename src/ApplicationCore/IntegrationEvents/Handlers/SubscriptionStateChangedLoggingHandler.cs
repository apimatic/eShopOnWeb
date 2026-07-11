using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

public class SubscriptionStateChangedLoggingHandler : INotificationHandler<SubscriptionStateChanged>
{
    private readonly IAppLogger<SubscriptionStateChangedLoggingHandler> _logger;

    public SubscriptionStateChangedLoggingHandler(IAppLogger<SubscriptionStateChangedLoggingHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} for {1} transitioned {2} -> {3}",
            notification.SubscriptionId, notification.BuyerId, notification.PreviousState, notification.NewState);
        return Task.CompletedTask;
    }
}
