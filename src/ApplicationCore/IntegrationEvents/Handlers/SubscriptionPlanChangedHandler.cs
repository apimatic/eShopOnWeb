using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// In-process reaction to UC3: audit the plan change and the amount that was agreed.
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
            "Subscription {0} moved from plan {1} to {2} ({3}); payment due {4} cents.",
            notification.Subscription.Id,
            notification.PreviousPlanHandle,
            notification.Subscription.PlanHandle,
            notification.Preview.Timing,
            notification.Preview.PaymentDueInCents);

        return Task.CompletedTask;
    }
}
