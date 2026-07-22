using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Sends the customer a confirmation once their subscription is active, reusing eShopOnWeb's
/// existing <see cref="IEmailSender"/> rather than introducing a second delivery mechanism.
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
        var price = notification.Price.ToString("C", CultureInfo.GetCultureInfo("en-US"));
        var nextBilling = notification.NextBillingAt.HasValue
            ? notification.NextBillingAt.Value.ToString("D", CultureInfo.GetCultureInfo("en-US"))
            : "the end of the current period";

        var body = $"You are now subscribed to {notification.PlanName ?? notification.PlanHandle} at {price} per period. " +
                   $"Your next billing date is {nextBilling}.";

        await _emailSender.SendEmailAsync(notification.UserReference, "Your eShopOnWeb subscription is active", body);
    }
}
