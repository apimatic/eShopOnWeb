using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

public class SubscriptionStateChangedHandler : INotificationHandler<SubscriptionStateChanged>
{
    private readonly IEmailSender _emailSender;
    private readonly IAppLogger<SubscriptionStateChangedHandler> _logger;

    public SubscriptionStateChangedHandler(IEmailSender emailSender, IAppLogger<SubscriptionStateChangedHandler> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} for user {1} changed state {2} -> {3}, effective {4}.",
            notification.SubscriptionId, notification.UserId, notification.OldState, notification.NewState, notification.EffectiveAt);

        await _emailSender.SendEmailAsync(notification.UserId, "Your eShopOnWeb subscription status changed",
            $"Your subscription is now {notification.NewState} (was {notification.OldState}), effective {notification.EffectiveAt:d}.");
    }
}
