using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

public class SubscriptionStateChangedAuditHandler : INotificationHandler<SubscriptionStateChanged>
{
    private readonly IAppLogger<SubscriptionStateChangedAuditHandler> _logger;

    public SubscriptionStateChangedAuditHandler(IAppLogger<SubscriptionStateChangedAuditHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} for user {1} changed state {2} -> {3}.",
            notification.SubscriptionId, notification.UserReference, notification.OldState, notification.NewState);
        return Task.CompletedTask;
    }
}
