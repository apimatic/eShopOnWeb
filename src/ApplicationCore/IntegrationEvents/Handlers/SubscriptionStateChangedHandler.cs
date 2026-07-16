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
        _logger.LogInformation("Subscription {SubscriptionId} state changed from {OldState} to {NewState}",
            notification.SubscriptionId, notification.OldState, notification.NewState);

        await _emailSender.SendEmailAsync(notification.CustomerReference, "Your subscription status changed",
            $"Your subscription moved from '{notification.OldState}' to '{notification.NewState}'.");
    }
}
