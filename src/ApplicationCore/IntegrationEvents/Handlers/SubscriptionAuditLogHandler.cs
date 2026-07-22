using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Writes an in-process audit trail for every subscription lifecycle change (plan §4.2).
/// Discovered by the MediatR assembly scan — no extra registration.
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
        _logger.LogInformation($"Subscription {notification.Subscription.BillingSubscriptionId} activated on plan '{notification.Subscription.PlanHandle}' for {notification.Subscription.UserReference}.");

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Subscription {notification.SubscriptionId} moved from plan '{notification.PreviousPlanHandle}' to '{notification.NewPlanHandle}' effective {notification.EffectiveAt:u}.");

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Subscription {notification.SubscriptionId} went from '{notification.PreviousState}' to '{notification.NewState}' via {notification.Action} effective {notification.EffectiveAt:u}.");

        return Task.CompletedTask;
    }
}
