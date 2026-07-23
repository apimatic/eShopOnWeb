using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Confirms a new subscription to the customer using eShopOnWeb's existing e-mail sender, and audits it
/// through the existing app logger (plan.md §2.5).
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
        _logger.LogInformation("Subscription {0} activated for {1} on plan '{2}' at {3} ({4}).",
            notification.SubscriptionId, notification.UserName, notification.PlanHandle,
            BillingMoney.ToDisplay(notification.PlanPrice), notification.State);

        await _emailSender.SendEmailAsync(
            notification.UserName,
            "Your eShopOnWeb subscription is active",
            $"You are subscribed to '{notification.PlanHandle}' at {BillingMoney.ToDisplay(notification.PlanPrice)}. " +
            $"Subscription reference: {notification.SubscriptionId}.");
    }
}
