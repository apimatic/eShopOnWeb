using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Writes an in-process audit trail for every subscription lifecycle change. Discovered by the
/// MediatR assembly scan in <see cref="Configuration.ConfigureWebServices"/> — no registration needed.
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
        _logger.LogInformation("Subscription {0} activated for {1} on plan {2} at {3}/{4}.",
            notification.Subscription.Id,
            notification.Subscription.BuyerId,
            notification.Subscription.Plan.Handle,
            notification.Subscription.Plan.Price,
            notification.Subscription.Plan.IntervalUnit);

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} moved from plan {1} to {2} ({3}); proration {4}.",
            notification.Subscription.Id,
            notification.PreviousPlanHandle,
            notification.NewPlanHandle,
            notification.Timing,
            notification.AppliedPreview?.ProratedAdjustment);

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} {1}: {2} -> {3}.",
            notification.Subscription.Id,
            notification.Action,
            notification.PreviousState,
            notification.NewState);

        return Task.CompletedTask;
    }
}
