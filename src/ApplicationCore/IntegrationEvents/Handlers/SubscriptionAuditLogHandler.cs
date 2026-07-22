using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Writes an audit trail of every subscription lifecycle fact through eShopOnWeb's existing logging
/// abstraction. This is the in-process reaction that is always present, in both hosts.
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
        var subscription = notification.Subscription;
        _logger.LogInformation(
            $"Subscription {subscription.ProviderSubscriptionId} activated for {subscription.CustomerReference} " +
            $"on plan {subscription.Plan.Handle} at {subscription.Plan.BillingDescription}; " +
            $"next billing {Describe(subscription.NextBillingAt)}.");

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        var subscription = notification.Subscription;
        _logger.LogInformation(
            $"Subscription {subscription.ProviderSubscriptionId} moved from plan {notification.PreviousPlanHandle} " +
            $"to {subscription.Plan.Handle} ({notification.Timing}); " +
            $"prorated adjustment {notification.Preview.ProratedAdjustment:C}.");

        return Task.CompletedTask;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        var subscription = notification.Subscription;
        _logger.LogInformation(
            $"Subscription {subscription.ProviderSubscriptionId} {notification.Action}: " +
            $"{notification.PreviousState} -> {notification.NewState}" +
            (subscription.CancelAtEndOfPeriod
                ? $", effective {Describe(subscription.CurrentPeriodEndsAt)}."
                : "."));

        return Task.CompletedTask;
    }

    private static string Describe(System.DateTimeOffset? moment) =>
        moment.HasValue ? moment.Value.ToString("u") : "unknown";
}
