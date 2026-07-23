using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Records a committed plan change in the application log (§2.5).
/// </summary>
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
            "Subscription {0} for {1} moved from plan {2} to {3} ({4}); {5} cents due.",
            notification.SubscriptionId,
            notification.BuyerId,
            notification.PreviousPlanHandle,
            notification.NewPlanHandle,
            notification.Timing,
            notification.PaymentDueInCents);

        return Task.CompletedTask;
    }
}
