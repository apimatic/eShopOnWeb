using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Writes a plan change to the audit log (plan.md §2.5).
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
            "Subscription {0} moved from plan {1} to {2} ({3}); net amount {4:C}.",
            notification.Subscription.Id,
            notification.PreviousPlan.Handle,
            notification.NewPlan.Handle,
            notification.Timing,
            notification.NetAmount);

        return Task.CompletedTask;
    }
}
