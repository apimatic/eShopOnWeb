using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Writes an audit trail of every subscription lifecycle change through the application's own
/// logging abstraction. This is the in-process reaction plan.md §2.5 calls for, and it is
/// deliberately the cheapest possible handler so it cannot itself become a source of failure.
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
        _logger.LogInformation("Subscription {0} activated for {1} on plan '{2}' at {3} per period; next billing {4}.",
            notification.SubscriptionId,
            notification.UserReference,
            notification.PlanHandle,
            notification.Price,
            notification.NextBillingAt?.ToString("u") ?? "unknown");

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} for {1} moved from '{2}' to '{3}' ({4}); proration {5}; effective {6}.",
            notification.SubscriptionId,
            notification.UserReference,
            notification.FromPlanHandle ?? "unknown",
            notification.ToPlanHandle,
            notification.Timing,
            notification.ProrationAmount,
            notification.EffectiveAt?.ToString("u") ?? "immediately");

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} for {1}: {2} moved state {3} -> {4}; effective {5}.",
            notification.SubscriptionId,
            notification.UserReference,
            notification.Action,
            notification.OldState,
            notification.NewState,
            notification.EffectiveAt?.ToString("u") ?? "immediately");

        return Task.CompletedTask;
    }
}
