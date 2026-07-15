using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

// Best-effort, in-process audit reaction to a plan change (see §2.5 of the integration plan).
public class SubscriptionPlanChangedHandler : INotificationHandler<SubscriptionPlanChanged>
{
    private readonly IAppLogger<SubscriptionPlanChangedHandler> _logger;

    public SubscriptionPlanChangedHandler(IAppLogger<SubscriptionPlanChangedHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {0} for customer {1} changed plan {2} -> {3} ({4}).",
            notification.SubscriptionId, notification.CustomerReference, notification.OldPlanHandle,
            notification.NewPlanHandle, notification.EffectiveImmediately ? "effective now" : "effective at renewal");

        return Task.CompletedTask;
    }
}
