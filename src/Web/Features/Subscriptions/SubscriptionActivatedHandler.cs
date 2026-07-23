using System.Globalization;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Confirms a new subscription in-process (§2.5) by emailing the customer and writing an audit line.
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
        _logger.LogInformation("Subscription {0} activated for {1} on plan {2}.",
            notification.SubscriptionId, notification.BuyerId, notification.PlanHandle);

        var price = (notification.PlanPriceInCents / 100m).ToString("C", CultureInfo.GetCultureInfo("en-US"));
        var nextBilling = notification.NextBillingDate.HasValue
            ? notification.NextBillingDate.Value.ToString("D", CultureInfo.InvariantCulture)
            : "not scheduled";

        await _emailSender.SendEmailAsync(
            notification.BuyerId,
            $"Your {notification.PlanName} subscription is active",
            $"You are subscribed to {notification.PlanName} at {price} per month. Your next billing date is {nextBilling}.");
    }
}
