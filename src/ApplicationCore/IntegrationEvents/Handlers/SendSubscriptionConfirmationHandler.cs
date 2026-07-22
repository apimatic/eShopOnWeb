using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Confirms a new subscription to the customer through eShopOnWeb's existing email sender.
/// </summary>
public class SendSubscriptionConfirmationHandler : INotificationHandler<SubscriptionActivated>
{
    private readonly IEmailSender _emailSender;

    public SendSubscriptionConfirmationHandler(IEmailSender emailSender)
    {
        _emailSender = emailSender;
    }

    public Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken)
    {
        var subscription = notification.Subscription;
        var message = $"You are now subscribed to {subscription.PlanName} at {subscription.PlanPrice:C}. Your next billing date is {subscription.NextBillingAt:d}.";

        return _emailSender.SendEmailAsync(subscription.UserReference, "Your eShopOnWeb subscription", message);
    }
}
