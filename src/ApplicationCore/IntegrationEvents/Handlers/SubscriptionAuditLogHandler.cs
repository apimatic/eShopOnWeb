using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Writes an audit trail of subscription lifecycle facts through the application's own logger -
/// the in-process reaction plan section 2.5 asks for.
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
            $"Subscription {notification.BillingSubscriptionId} activated for {notification.BuyerId} on plan {notification.PlanHandle}.");
        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            $"Subscription {notification.BillingSubscriptionId} moved from {notification.OldPlanHandle} to {notification.NewPlanHandle}.");
        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            $"Subscription {notification.BillingSubscriptionId} went {notification.OldState} -> {notification.NewState} via {notification.Action}.");
        return Task.CompletedTask;
    }
}
