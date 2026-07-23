using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Writes an audit trail for every subscription lifecycle change, using eShopOnWeb's existing
/// logging abstraction. Runs in-process, after the provider call already succeeded.
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
            $"Subscription {notification.Subscription.Id} activated for {notification.UserReference} " +
            $"on plan {notification.Subscription.PlanHandle}.");

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            $"Subscription {notification.Subscription.Id} for {notification.UserReference} moved from plan " +
            $"{notification.PreviousPlanHandle} to {notification.NewPlanHandle} ({notification.Timing}).");

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            $"Subscription {notification.Subscription.Id} for {notification.UserReference} went from " +
            $"{notification.PreviousStatus} to {notification.NewStatus} via {notification.Action}.");

        return Task.CompletedTask;
    }
}
