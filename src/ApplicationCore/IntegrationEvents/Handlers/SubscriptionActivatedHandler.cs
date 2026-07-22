using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Confirms a new subscription to the customer and records it for audit. Runs in-process off the
/// <see cref="SubscriptionActivated"/> notification (UC1, step 6).
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

        _logger.LogInformation(
            "Subscription {SubscriptionId} activated for {UserName} on plan {PlanHandle} at {Price:C}.",
            subscription.Id, notification.UserName, subscription.PlanHandle ?? "(unknown)", subscription.PlanPrice);

        var nextBilling = subscription.NextAssessmentAt.HasValue
            ? subscription.NextAssessmentAt.Value.ToString("D")
            : "the end of the current period";

        await _emailSender.SendEmailAsync(
            notification.UserName,
            "Your eShopOnWeb subscription is active",
            $"You are now subscribed to {subscription.PlanName ?? subscription.PlanHandle} " +
            $"at {subscription.PlanPrice:C}. Your next billing date is {nextBilling}.");
    }
}
