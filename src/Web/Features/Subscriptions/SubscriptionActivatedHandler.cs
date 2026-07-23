using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Confirms a new subscription to the customer and records it in the audit log.
/// </summary>
/// <remarks>
/// In-process reaction to <see cref="SubscriptionActivated"/>, using the same
/// <see cref="IEmailSender"/> and <see cref="IAppLogger{T}"/> the rest of the app already uses.
/// </remarks>
public class SubscriptionActivatedHandler : INotificationHandler<SubscriptionActivated>
{
    private readonly IEmailSender _emailSender;
    private readonly IAppLogger<SubscriptionActivatedHandler> _logger;

    public SubscriptionActivatedHandler(
        IEmailSender emailSender,
        IAppLogger<SubscriptionActivatedHandler> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken)
    {
        var subscription = notification.Subscription;

        _logger.LogInformation(
            "Subscription {SubscriptionId} activated for {UserReference} on plan {PlanHandle}.",
            subscription.Id,
            notification.UserReference,
            subscription.PlanHandle);

        await _emailSender.SendEmailAsync(
            notification.UserReference,
            "Your eShopOnWeb subscription is active",
            $"You are now subscribed to {subscription.PlanName ?? subscription.PlanHandle}. " +
            $"Your next billing date is {subscription.NextAssessmentAt:d}.");
    }
}
