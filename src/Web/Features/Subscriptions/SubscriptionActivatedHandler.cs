using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Confirms a new subscription in-process: emails the customer and writes an audit line
/// (plan.md §2.5).
/// </summary>
public class SubscriptionActivatedHandler : INotificationHandler<SubscriptionActivated>
{
    private readonly IEmailSender _emailSender;
    private readonly IAppLogger<SubscriptionActivatedHandler> _logger;

    public SubscriptionActivatedHandler(IEmailSender emailSender,
        IAppLogger<SubscriptionActivatedHandler> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken)
    {
        var subscription = notification.Subscription;

        _logger.LogInformation("Subscription {Id} activated for {Customer} on plan {Plan} at {Price:C}",
            subscription.Id, subscription.CustomerReference, subscription.PlanHandle, subscription.PlanPrice);

        await _emailSender.SendEmailAsync(subscription.CustomerReference,
            $"Your {subscription.PlanName} subscription is active",
            $"Thanks for subscribing to {subscription.PlanName} at {subscription.PlanPrice:C} per month. " +
            $"Your next billing date is {subscription.CurrentPeriodEndsAt:d}.");
    }
}
