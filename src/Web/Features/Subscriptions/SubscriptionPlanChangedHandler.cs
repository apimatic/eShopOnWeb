using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>Audits a plan change in-process, recording old plan, new plan and the proration applied.</summary>
public class SubscriptionPlanChangedHandler : INotificationHandler<SubscriptionPlanChanged>
{
    private readonly IAppLogger<SubscriptionPlanChangedHandler> _logger;

    public SubscriptionPlanChangedHandler(IAppLogger<SubscriptionPlanChangedHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        var effective = notification.Timing == PlanChangeTiming.Immediate
            ? "immediately"
            : "at the next renewal";

        _logger.LogInformation(
            $"Subscription {notification.Subscription.Id} for {notification.UserName} moved from {notification.PreviousPlan.Handle} to {notification.NewPlan.Handle} {effective}; {notification.PaymentDueInCents} cents due.");

        return Task.CompletedTask;
    }
}
