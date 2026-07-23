using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Writes an audit trail for every subscription lifecycle change, using eShopOnWeb's existing
/// <see cref="IAppLogger{T}"/> abstraction (plan.md §2.5).
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
            "Subscription {0} activated for {1} on plan {2} at {3:N2} per period; next billing {4}.",
            notification.SubscriptionId,
            notification.CustomerReference,
            notification.PlanHandle,
            notification.PlanPrice,
            notification.NextBillingDate?.ToString("u") ?? "unknown");

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {0} for {1} moved from plan {2} to {3} ({4}); proration {5}.",
            notification.SubscriptionId,
            notification.CustomerReference,
            notification.PreviousPlanHandle ?? "(unknown)",
            notification.NewPlanHandle,
            notification.Timing,
            notification.PaymentDue?.ToString("N2") ?? "none");

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {0} for {1} transitioned {2} -> {3} via {4}, effective {5}.",
            notification.SubscriptionId,
            notification.CustomerReference,
            notification.PreviousState,
            notification.NewState,
            notification.Action,
            notification.EffectiveAt?.ToString("u") ?? "immediately");

        return Task.CompletedTask;
    }
}
