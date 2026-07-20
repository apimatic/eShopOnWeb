using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

public class SubscriptionActivatedAuditHandler : INotificationHandler<SubscriptionActivated>
{
    private readonly IAppLogger<SubscriptionActivatedAuditHandler> _logger;

    public SubscriptionActivatedAuditHandler(IAppLogger<SubscriptionActivatedAuditHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} activated for user {1} on plan {2}.", notification.SubscriptionId, notification.UserReference, notification.ProductHandle);
        return Task.CompletedTask;
    }
}
