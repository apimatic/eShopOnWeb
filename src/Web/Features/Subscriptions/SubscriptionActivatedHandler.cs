using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// In-process reaction to a successful enrollment (UC1): sends a confirmation email and writes
/// an audit log entry. Best-effort - see plan.md §2.5; failures here never roll back the subscription.
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
        _logger.LogInformation("Subscription {0} activated on plan {1} for {2}",
            notification.SubscriptionId, notification.ProductHandle, notification.CustomerReference);

        await _emailSender.SendEmailAsync(
            notification.CustomerReference,
            "Your subscription is active",
            $"You're subscribed to {notification.ProductHandle}. Thanks for subscribing!");
    }
}
