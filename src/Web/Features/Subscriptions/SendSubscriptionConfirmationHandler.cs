using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Emails the customer their subscription confirmation, using the storefront's existing sender.
/// </summary>
/// <remarks>
/// One of the in-process reactions to a lifecycle notification. It lives in the Web host because
/// that is where <see cref="IEmailSender"/> is registered, and it runs best-effort: the enrollment
/// is already committed at the provider before this is ever invoked.
/// </remarks>
public class SendSubscriptionConfirmationHandler : INotificationHandler<SubscriptionActivated>
{
    private readonly IEmailSender _emailSender;

    public SendSubscriptionConfirmationHandler(IEmailSender emailSender)
    {
        _emailSender = emailSender;
    }

    public async Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken)
    {
        var subscription = notification.Subscription;

        if (string.IsNullOrWhiteSpace(subscription.CustomerReference))
        {
            return;
        }

        var body =
            $"You are now subscribed to {subscription.Plan.Name} at {subscription.Plan.BillingDescription}. " +
            $"Your subscription is {subscription.State}" +
            (subscription.NextBillingAt.HasValue
                ? $" and renews on {subscription.NextBillingAt.Value:d MMMM yyyy}."
                : ".");

        await _emailSender.SendEmailAsync(subscription.CustomerReference,
            $"Your {subscription.Plan.Name} subscription", body);
    }
}
