using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Records a completed plan change for audit. Runs in-process off the
/// <see cref="SubscriptionPlanChanged"/> notification (UC3, step 5).
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
            "Subscription {SubscriptionId} moved from plan {PreviousPlanHandle} to {NewPlanHandle} ({Timing}); amount due {ProrationAmount:C}.",
            notification.Subscription.Id,
            notification.PreviousPlanHandle,
            notification.NewPlanHandle,
            notification.Timing,
            notification.ProrationAmount);

        return Task.CompletedTask;
    }
}
