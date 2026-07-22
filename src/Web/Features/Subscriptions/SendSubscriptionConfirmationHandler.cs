using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Emails the customer their subscription confirmation, reusing the storefront's existing
/// <see cref="IEmailSender"/>. In-process and best-effort (plan.md §2.5).
/// </summary>
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

        await _emailSender.SendEmailAsync(subscription.CustomerReference,
            $"Your {subscription.PlanName} subscription is active",
            $"You are subscribed to {subscription.PlanName} at $ {subscription.PlanPrice:N2} per month. " +
            $"Your next billing date is {subscription.CurrentPeriodEndsAt:d}.");
    }
}
