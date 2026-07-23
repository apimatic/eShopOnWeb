using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Sends the customer a confirmation when their subscription goes live, through eShopOnWeb's
/// existing <see cref="IEmailSender"/> abstraction.
/// </summary>
/// <remarks>
/// Eventing is best-effort (there is no durable outbox), so a failure to send the confirmation
/// is logged and swallowed — the subscription itself already stands.
/// </remarks>
public class SubscriptionConfirmationEmailHandler : INotificationHandler<SubscriptionActivated>
{
    private readonly IEmailSender _emailSender;
    private readonly IAppLogger<SubscriptionConfirmationEmailHandler> _logger;

    public SubscriptionConfirmationEmailHandler(
        IEmailSender emailSender,
        IAppLogger<SubscriptionConfirmationEmailHandler> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken)
    {
        var subscription = notification.Subscription;

        var nextBilling = subscription.NextAssessmentAt.HasValue
            ? subscription.NextAssessmentAt.Value.ToString("d", CultureInfo.InvariantCulture)
            : "not scheduled";

        var body =
            $"Your {subscription.PlanName} subscription is active. " +
            $"You will be billed ${subscription.PlanPrice.ToString("N2", CultureInfo.InvariantCulture)} " +
            $"per period, next on {nextBilling}.";

        try
        {
            await _emailSender.SendEmailAsync(
                notification.UserName,
                $"Your {subscription.PlanName} subscription is active",
                body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to send the subscription confirmation for subscription {0}: {1}",
                subscription.Id,
                ex.Message);
        }
    }
}
