using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// In-process reaction to UC1: audit the enrollment and confirm it to the customer by email.
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
            "Subscription {0} activated for {1} on plan {2} ({3}).",
            subscription.Id,
            subscription.CustomerReference,
            subscription.PlanHandle,
            subscription.State);

        var nextBilling = subscription.NextAssessmentAt?.ToString("d", CultureInfo.InvariantCulture) ?? "not scheduled";

        await _emailSender.SendEmailAsync(
            subscription.CustomerReference,
            "Your eShopOnWeb subscription is active",
            $"You are subscribed to {subscription.PlanName} at {subscription.PlanPrice.ToString("C", CultureInfo.InvariantCulture)}. Next billing date: {nextBilling}.");
    }
}
