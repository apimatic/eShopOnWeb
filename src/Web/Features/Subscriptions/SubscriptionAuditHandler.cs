using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Audits plan changes and lifecycle transitions through eShopOnWeb's existing logging abstraction
/// (plan.md §2.5). Deliberately does nothing else — durable, cross-process delivery is out of scope.
/// </summary>
public class SubscriptionAuditHandler :
    INotificationHandler<SubscriptionPlanChanged>,
    INotificationHandler<SubscriptionStateChanged>
{
    private readonly IAppLogger<SubscriptionAuditHandler> _logger;

    public SubscriptionAuditHandler(IAppLogger<SubscriptionAuditHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} for {1} moved from '{2}' to '{3}' ({4}), net {5}.",
            notification.SubscriptionId,
            notification.UserName,
            notification.PreviousPlanHandle,
            notification.NewPlanHandle,
            notification.AppliedImmediately ? "immediately" : "at next renewal",
            BillingMoney.ToSignedDisplay(notification.ProrationAmount));

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} for {1}: {2} → {3} via {4}, effective {5}.",
            notification.SubscriptionId,
            notification.UserName,
            notification.PreviousState,
            notification.NewState,
            notification.Action,
            notification.EffectiveAt?.ToString("u") ?? "immediately");

        return Task.CompletedTask;
    }
}
