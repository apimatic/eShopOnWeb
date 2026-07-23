using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Writes an in-process audit trail of every subscription lifecycle change, using eShopOnWeb's
/// existing <see cref="IAppLogger{T}"/> abstraction.
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
            "Subscription {0} activated for {1} on plan {2} at {3} cents per period.",
            notification.Subscription.Id,
            notification.UserName,
            notification.Subscription.PlanHandle,
            notification.Subscription.PlanPriceInCents);

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {0} changed plan from {1} to {2} ({3}) for {4}.",
            notification.Subscription.Id,
            notification.PreviousPlanHandle,
            notification.Subscription.PlanHandle,
            notification.Timing,
            DescribeActor(notification.UserName));

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {0} transitioned {1} -> {2} via {3} for {4}.",
            notification.Subscription.Id,
            notification.PreviousState,
            notification.NewState,
            notification.Action,
            DescribeActor(notification.UserName));

        return Task.CompletedTask;
    }

    /// <summary>Names the actor in the audit trail; administrator actions carry no user name.</summary>
    private static string DescribeActor(string? userName) => userName ?? "an administrator";
}
