using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Writes an audit trail for every subscription lifecycle change (plan.md §2.5). Lives in
/// ApplicationCore so both hosts get the same audit record from the one registration.
/// </summary>
public class SubscriptionAuditLogHandler :
    INotificationHandler<SubscriptionActivated>,
    INotificationHandler<SubscriptionPlanChanged>,
    INotificationHandler<SubscriptionStateChanged>
{
    private readonly IAppLogger<SubscriptionAuditLogHandler> _logger;

    public SubscriptionAuditLogHandler(IAppLogger<SubscriptionAuditLogHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken)
    {
        var subscription = notification.Subscription;
        _logger.LogInformation("Subscription {0} activated for {1} on plan {2} at $ {3:N2}.",
            subscription.Id, subscription.CustomerReference, subscription.PlanHandle, subscription.PlanPrice);

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        var subscription = notification.Subscription;
        _logger.LogInformation("Subscription {0} moved from plan {1} to {2} ({3}).",
            subscription.Id, notification.PreviousPlanHandle, subscription.PlanHandle, notification.Timing);

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} went from {1} to {2} via '{3}'.",
            notification.Subscription.Id, notification.PreviousState, notification.NewState, notification.Action);

        return Task.CompletedTask;
    }
}
