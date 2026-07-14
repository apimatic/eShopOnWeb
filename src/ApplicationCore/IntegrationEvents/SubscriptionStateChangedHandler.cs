using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

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
            "Subscription {0} for {1} transitioned {2} -> {3}",
            notification.SubscriptionId, notification.BuyerId, notification.OldState, notification.NewState);

        return Task.CompletedTask;
    }
}
