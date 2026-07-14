using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

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
        _logger.LogInformation("Subscription {0} activated for {1} on plan {2}", notification.SubscriptionId, notification.BuyerId, notification.ProductHandle);

        await _emailSender.SendEmailAsync(
            notification.BuyerId,
            "Your subscription is active",
            $"You are now subscribed to {notification.ProductName} at {notification.PriceInCents / 100m:C} per billing period.");
    }
}
