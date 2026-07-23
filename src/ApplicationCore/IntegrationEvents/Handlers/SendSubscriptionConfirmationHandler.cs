using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Emails the customer their subscription confirmation through eShopOnWeb's existing sender.
/// A send failure is logged and swallowed — the subscription is already active at the provider
/// and must not be undone by a mail problem (plan section 2.5).
/// </summary>
public class SendSubscriptionConfirmationHandler : INotificationHandler<SubscriptionActivated>
{
    private readonly IEmailSender _emailSender;
    private readonly IAppLogger<SendSubscriptionConfirmationHandler> _logger;

    public SendSubscriptionConfirmationHandler(IEmailSender emailSender,
        IAppLogger<SendSubscriptionConfirmationHandler> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken)
    {
        var subscription = notification.Subscription;

        var nextBillingDate = subscription.CurrentPeriodEndsAt.HasValue
            ? subscription.CurrentPeriodEndsAt.Value.ToString("D")
            : "not scheduled";

        try
        {
            await _emailSender.SendEmailAsync(notification.UserReference,
                "Your eShopOnWeb subscription is active",
                $"You are now subscribed to {subscription.PlanName ?? subscription.PlanHandle} at " +
                $"${subscription.PlanPrice:N2}. Your next billing date is {nextBillingDate}.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                $"Subscription {subscription.Id} is active but the confirmation email to " +
                $"{notification.UserReference} could not be sent: {exception.Message}");
        }
    }
}
