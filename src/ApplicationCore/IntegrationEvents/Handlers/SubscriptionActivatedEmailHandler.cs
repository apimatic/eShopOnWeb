using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>Sends a confirmation email and writes an audit log entry when a subscription activates.</summary>
public class SubscriptionActivatedEmailHandler : INotificationHandler<SubscriptionActivated>
{
    private readonly IEmailSender _emailSender;
    private readonly IAppLogger<SubscriptionActivatedEmailHandler> _logger;

    public SubscriptionActivatedEmailHandler(IEmailSender emailSender, IAppLogger<SubscriptionActivatedEmailHandler> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken)
    {
        await _emailSender.SendEmailAsync(
            notification.UserName,
            "Your eShopOnWeb subscription is active",
            $"You're subscribed to plan '{notification.ProductHandle}' at {notification.PriceInCents / 100m:C}/month. Next billing date: {notification.NextAssessmentAt:d}.");

        _logger.LogInformation("Subscription {SubscriptionId} activated for {UserName} on plan {ProductHandle}",
            notification.SubscriptionId, notification.UserName, notification.ProductHandle);
    }
}
