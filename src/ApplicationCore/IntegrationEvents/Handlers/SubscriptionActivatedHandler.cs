using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Confirms a new subscription to the customer and records it in the application log.
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

        _logger.LogInformation("Subscription {SubscriptionId} activated for {UserReference} on plan {PlanHandle}.",
            subscription.Id, notification.UserReference, subscription.PlanHandle);

        var nextBilling = subscription.CurrentPeriodEndsAt.HasValue
            ? subscription.CurrentPeriodEndsAt.Value.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
            : "the end of the current period";

        var message =
            $"You are now subscribed to {subscription.PlanName ?? subscription.PlanHandle} at " +
            $"${subscription.PlanPrice.ToString("N2", CultureInfo.InvariantCulture)} per period. " +
            $"Your next billing date is {nextBilling}.";

        await _emailSender.SendEmailAsync(notification.UserReference, "Your eShopOnWeb subscription is active", message);
    }
}
