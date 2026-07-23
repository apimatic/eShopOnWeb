using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// The in-process reaction to subscription lifecycle facts (§2.5): an audit line through the
/// existing <see cref="IAppLogger{T}"/> and a confirmation through the existing
/// <see cref="IEmailSender"/>. Delivery is best-effort — nothing here can undo the provider-side
/// change that already happened.
/// </summary>
public class SubscriptionNotificationHandler :
    INotificationHandler<SubscriptionActivated>,
    INotificationHandler<SubscriptionPlanChanged>,
    INotificationHandler<SubscriptionStateChanged>
{
    private readonly IAppLogger<SubscriptionNotificationHandler> _logger;
    private readonly IEmailSender _emailSender;

    public SubscriptionNotificationHandler(IAppLogger<SubscriptionNotificationHandler> logger,
        IEmailSender emailSender)
    {
        _logger = logger;
        _emailSender = emailSender;
    }

    public async Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken)
    {
        var subscription = notification.Subscription;

        _logger.LogInformation("Subscription {0} activated for {1} on plan {2} at {3:N2}/{4}.",
            subscription.Id, subscription.UserReference, subscription.PlanHandle, subscription.PlanPrice,
            subscription.IntervalUnit);

        await _emailSender.SendEmailAsync(subscription.UserReference, "Your subscription is active",
            $"You are subscribed to {subscription.PlanName} at {subscription.PlanPrice:N2} per {subscription.IntervalUnit}. Your next billing date is {subscription.CurrentPeriodEndsAt:d}.");
    }

    public async Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        var subscription = notification.Subscription;

        _logger.LogInformation("Subscription {0} moved from plan {1} to {2} ({3}).",
            subscription.Id, notification.PreviousPlanHandle, subscription.PlanHandle, notification.Timing);

        await _emailSender.SendEmailAsync(subscription.UserReference, "Your plan has changed",
            $"Your subscription moved from {notification.PreviousPlanHandle} to {subscription.PlanName}, effective {notification.Timing}.");
    }

    public async Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        var subscription = notification.Subscription;

        _logger.LogInformation("Subscription {0} changed state from {1} to {2}.",
            subscription.Id, notification.PreviousState, notification.NewState);

        await _emailSender.SendEmailAsync(subscription.UserReference, "Your subscription was updated",
            $"Your subscription is now {notification.NewState}.");
    }
}
