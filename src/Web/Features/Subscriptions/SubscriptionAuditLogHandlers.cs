using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// In-process audit-log reactions to subscription lifecycle notifications (§2.5). One handler per
/// notification type, mirroring the plan's example of "write an audit log via IAppLogger&lt;&gt;".
/// </summary>
public class SubscriptionActivatedAuditHandler : INotificationHandler<SubscriptionActivated>
{
    private readonly IAppLogger<SubscriptionActivatedAuditHandler> _logger;

    public SubscriptionActivatedAuditHandler(IAppLogger<SubscriptionActivatedAuditHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {SubscriptionId} activated for user {UserReference} on plan {PlanHandle}.",
            notification.SubscriptionId, notification.UserReference, notification.PlanHandle);
        return Task.CompletedTask;
    }
}

public class SubscriptionPlanChangedAuditHandler : INotificationHandler<SubscriptionPlanChanged>
{
    private readonly IAppLogger<SubscriptionPlanChangedAuditHandler> _logger;

    public SubscriptionPlanChangedAuditHandler(IAppLogger<SubscriptionPlanChangedAuditHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {SubscriptionId} for user {UserReference} changed plan {OldPlanHandle} -> {NewPlanHandle} ({Timing}), effective {EffectiveDate}.",
            notification.SubscriptionId, notification.UserReference, notification.OldPlanHandle, notification.NewPlanHandle,
            notification.AppliedImmediately ? "now, prorated" : "at next renewal", notification.EffectiveDate);
        return Task.CompletedTask;
    }
}

public class SubscriptionStateChangedAuditHandler : INotificationHandler<SubscriptionStateChanged>
{
    private readonly IAppLogger<SubscriptionStateChangedAuditHandler> _logger;

    public SubscriptionStateChangedAuditHandler(IAppLogger<SubscriptionStateChangedAuditHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {SubscriptionId} for user {UserReference} transitioned {OldState} -> {NewState}.",
            notification.SubscriptionId, notification.UserReference, notification.OldState, notification.NewState);
        return Task.CompletedTask;
    }
}
