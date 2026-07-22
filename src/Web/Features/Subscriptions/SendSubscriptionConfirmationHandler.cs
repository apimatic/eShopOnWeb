using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Confirms a new subscription to the customer through the existing <see cref="IEmailSender"/>.
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

        var message = $"You are subscribed to {subscription.Plan.Name} at " +
            $"{subscription.Plan.Price:C} per {subscription.Plan.IntervalUnit}. " +
            $"Your next billing date is {subscription.CurrentPeriodEndsAt:d}.";

        await _emailSender.SendEmailAsync(subscription.BuyerId, "Your eShopOnWeb subscription", message);
    }
}
