using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>
/// Best-effort in-process reaction to a new subscription: emails a confirmation and writes an
/// audit log entry. Never throws — a handler failure must not affect the subscription, which has
/// already been committed with the billing provider by the time this runs (§2.5: best-effort eventing).
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
        try
        {
            await _emailSender.SendEmailAsync(
                notification.CustomerReference,
                "Your subscription is active",
                $"You are now subscribed to {notification.PlanHandle} (subscription #{notification.SubscriptionId}).");

            _logger.LogInformation(
                "Subscription {0} activated for {1} on plan {2}",
                notification.SubscriptionId, notification.CustomerReference, notification.PlanHandle);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "SubscriptionActivated handler failed for subscription {0}: {1}",
                notification.SubscriptionId, ex.Message);
        }
    }
}
