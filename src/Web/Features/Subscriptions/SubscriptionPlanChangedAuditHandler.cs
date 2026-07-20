using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

public class SubscriptionPlanChangedAuditHandler : INotificationHandler<SubscriptionPlanChanged>
{
    private readonly IAppLogger<SubscriptionPlanChangedAuditHandler> _logger;

    public SubscriptionPlanChangedAuditHandler(IAppLogger<SubscriptionPlanChangedAuditHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} for user {1} changed plan {2} -> {3} ({4}).",
            notification.SubscriptionId, notification.UserReference, notification.OldProductHandle, notification.NewProductHandle,
            notification.AppliedNow ? "applied now" : "scheduled at renewal");
        return Task.CompletedTask;
    }
}
