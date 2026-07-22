using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Records a committed plan change in the application log.
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
        var result = notification.Result;

        _logger.LogInformation("Subscription {SubscriptionId} moved from plan {PreviousPlan} to {TargetPlan} ({Timing}); amount applied {Amount}.",
            result.Subscription.Id,
            result.PreviousPlanHandle ?? "(none)",
            result.TargetPlanHandle,
            result.Timing,
            "$" + result.AmountApplied.ToString("N2", CultureInfo.InvariantCulture));

        return Task.CompletedTask;
    }
}
