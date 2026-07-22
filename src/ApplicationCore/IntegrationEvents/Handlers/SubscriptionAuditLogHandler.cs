using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Writes an audit trail for every subscription lifecycle notification through eShopOnWeb's existing
/// <see cref="IAppLogger{T}"/> abstraction (plan §2.5).
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
        _logger.LogInformation(
            "Subscription {SubscriptionId} activated on plan {PlanHandle} for customer reference {UserReference}.",
            notification.Subscription.Id,
            notification.Subscription.PlanHandle ?? "(unknown)",
            notification.UserReference);

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {SubscriptionId} changed plan from {PreviousPlanHandle} to {NewPlanHandle} ({Timing}); payment due {PaymentDue}.",
            notification.SubscriptionId,
            notification.PreviousPlanHandle,
            notification.NewPlanHandle,
            notification.Timing,
            notification.AppliedPaymentDue);

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {SubscriptionId} transitioned from {PreviousState} to {NewState} via {Action}.",
            notification.SubscriptionId,
            notification.PreviousState,
            notification.NewState,
            notification.Action);

        return Task.CompletedTask;
    }
}
