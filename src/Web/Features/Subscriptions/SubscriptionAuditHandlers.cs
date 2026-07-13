using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// In-process reactions to subscription lifecycle notifications (§2.5) — audit logging via
/// <see cref="IAppLogger{T}"/>. Best-effort: a handler failure never rolls back the subscription
/// change that already succeeded with the provider.
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
        _logger.LogInformation(
            "Subscription {0} activated for customer {1} on plan {2} ({3} cents)",
            notification.SubscriptionId, notification.CustomerReference, notification.ProductHandle, notification.PriceInCents);
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
        _logger.LogInformation(
            "Subscription {0} for customer {1} changed plan {2} -> {3} (proration {4} cents)",
            notification.SubscriptionId, notification.CustomerReference, notification.FromProductHandle, notification.ToProductHandle, notification.ProratedAdjustmentInCents);
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
        _logger.LogInformation(
            "Subscription {0} for customer {1} changed state {2} -> {3}",
            notification.SubscriptionId, notification.CustomerReference, notification.OldState, notification.NewState);
        return Task.CompletedTask;
    }
}
