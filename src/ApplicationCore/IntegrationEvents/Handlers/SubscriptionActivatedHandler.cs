using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Confirms a new subscription to the customer and records it in the audit log.
/// </summary>
/// <remarks>
/// The enrolment has already succeeded at the provider by the time this runs, so a failure here
/// must not surface as a failed subscribe (plan.md §2.5, UC1 failure scenarios). Every fault is
/// swallowed and logged.
/// </remarks>
public class SubscriptionActivatedHandler : INotificationHandler<SubscriptionActivated>
{
    private readonly IEmailSender _emailSender;
    private readonly IAppLogger<SubscriptionActivatedHandler> _logger;

    public SubscriptionActivatedHandler(IEmailSender emailSender,
        IAppLogger<SubscriptionActivatedHandler> logger)
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
            subscription.UserReference,
            subscription.Plan.Handle,
            subscription.Plan.PriceDescription);

        try
        {
            var nextBilling = subscription.NextAssessmentAt.HasValue
                ? subscription.NextAssessmentAt.Value.ToString("D", CultureInfo.InvariantCulture)
                : "not scheduled yet";

            await _emailSender.SendEmailAsync(
                subscription.UserReference,
                $"Your {subscription.Plan.Name} subscription is active",
                $"You are subscribed to {subscription.Plan.Name} at {subscription.Plan.PriceDescription}. " +
                $"Next billing date: {nextBilling}.");
        }
        catch (Exception ex)
        {
            // Best-effort eventing: the subscription stands even if the confirmation cannot be sent.
            _logger.LogWarning(
                "Could not send the activation confirmation for subscription {0}: {1}",
                subscription.Id,
                ex.Message);
        }
    }
}
