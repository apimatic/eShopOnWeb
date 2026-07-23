using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Confirms a new subscription in-process: an audit line and a confirmation email through the
/// existing <see cref="IEmailSender"/>. Discovered by the assembly scan in AddWebServices.
/// </summary>
public class SubscriptionActivatedHandler : INotificationHandler<SubscriptionActivated>
{
    private readonly IEmailSender _emailSender;
    private readonly IAppLogger<SubscriptionActivatedHandler> _logger;

    public SubscriptionActivatedHandler(IEmailSender emailSender, IAppLogger<SubscriptionActivatedHandler> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken)
    {
        var subscription = notification.Subscription;

        _logger.LogInformation(
            $"Subscription {subscription.Id} activated for {notification.UserName} on plan {subscription.Plan.Handle} at {subscription.Plan.PriceInCents} cents per {subscription.Plan.BillingPeriod}.");

        await _emailSender.SendEmailAsync(notification.UserName,
            $"Your {subscription.Plan.Name} subscription is active",
            $"You are now subscribed to {subscription.Plan.Name} at ${subscription.Plan.Price:N2} per {subscription.Plan.BillingPeriod}. "
            + $"Your next billing date is {subscription.NextAssessmentAt:d}.");
    }
}
