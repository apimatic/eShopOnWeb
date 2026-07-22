using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Writes an in-process audit trail for every subscription lifecycle fact, through eShopOnWeb's own
/// logging abstraction. This is the reference in-process reaction described in the eventing
/// convention: it demonstrates that handlers run, without introducing any durable delivery.
/// </summary>
public class SubscriptionAuditHandler :
    INotificationHandler<SubscriptionActivated>,
    INotificationHandler<SubscriptionPlanChanged>,
    INotificationHandler<SubscriptionStateChanged>
{
    private readonly IAppLogger<SubscriptionAuditHandler> _logger;

    public SubscriptionAuditHandler(IAppLogger<SubscriptionAuditHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {0} activated for {1} on plan {2} at {3} per period; next billing {4}.",
            notification.SubscriptionId,
            notification.UserName,
            notification.PlanHandle,
            notification.PlanPrice.ToString("C2", CultureInfo.InvariantCulture),
            notification.NextBillingDate?.ToString("u", CultureInfo.InvariantCulture) ?? "unknown");

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {0} moved from plan {1} to {2} ({3}); proration {4}; effective {5}.",
            notification.SubscriptionId,
            notification.PreviousPlanHandle,
            notification.NewPlanHandle,
            notification.Timing,
            notification.ProrationAmount.ToString("C2", CultureInfo.InvariantCulture),
            notification.EffectiveAt?.ToString("u", CultureInfo.InvariantCulture) ?? "immediately");

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {0} transitioned {1} -> {2} via {3}; effective {4}.",
            notification.SubscriptionId,
            notification.PreviousState,
            notification.NewState,
            notification.Action,
            notification.EffectiveAt?.ToString("u", CultureInfo.InvariantCulture) ?? "immediately");

        return Task.CompletedTask;
    }
}
