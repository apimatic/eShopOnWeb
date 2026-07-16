using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

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
        _logger.LogInformation("Subscription {SubscriptionId} activated for {CustomerReference} on plan {ProductHandle}",
            notification.SubscriptionId, notification.CustomerReference, notification.ProductHandle);

        await _emailSender.SendEmailAsync(notification.CustomerReference, "Your subscription is active",
            $"Your subscription to plan '{notification.ProductHandle}' is now active.");
    }
}
