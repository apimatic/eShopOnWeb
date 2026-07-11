using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

public class SubscriptionActivatedLoggingHandler : INotificationHandler<SubscriptionActivated>
{
    private readonly IAppLogger<SubscriptionActivatedLoggingHandler> _logger;

    public SubscriptionActivatedLoggingHandler(IAppLogger<SubscriptionActivatedLoggingHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} activated for {1} on plan {2}", notification.SubscriptionId, notification.BuyerId, notification.ProductHandle);
        return Task.CompletedTask;
    }
}
