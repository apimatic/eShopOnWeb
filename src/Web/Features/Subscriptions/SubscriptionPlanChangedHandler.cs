using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Audits a plan change in-process (plan.md §2.5).
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
        _logger.LogInformation("Subscription {Id} moved from {OldPlan} to {NewPlan} ({Timing}); payment due {Due:C}",
            notification.Subscription.Id, notification.PreviousPlanHandle, notification.Subscription.PlanHandle,
            notification.Timing, notification.AppliedPreview.PaymentDue);

        return Task.CompletedTask;
    }
}
