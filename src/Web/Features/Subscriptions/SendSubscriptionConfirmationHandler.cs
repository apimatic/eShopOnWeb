using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Sends the customer a confirmation when their subscription becomes active, using eShopOnWeb's existing
/// <see cref="IEmailSender"/> (plan.md §2.5 — an in-process reaction to the lifecycle notification).
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
        var nextBilling = notification.NextBillingDate?.LocalDateTime.ToString("d") ?? "not yet scheduled";

        await _emailSender.SendEmailAsync(
            notification.CustomerReference,
            "Your eShopOnWeb subscription is active",
            $"You are subscribed to {notification.PlanName ?? notification.PlanHandle} " +
            $"at ${notification.PlanPrice:N2} per period. Next billing date: {nextBilling}.");
    }
}
