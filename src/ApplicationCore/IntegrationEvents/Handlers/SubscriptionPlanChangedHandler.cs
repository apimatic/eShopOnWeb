using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

public class SubscriptionPlanChangedHandler : INotificationHandler<SubscriptionPlanChanged>
{
    private readonly IEmailSender _emailSender;
    private readonly IAppLogger<SubscriptionPlanChangedHandler> _logger;

    public SubscriptionPlanChangedHandler(IEmailSender emailSender, IAppLogger<SubscriptionPlanChangedHandler> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {SubscriptionId} plan changed from {FromProductHandle} to {ToProductHandle}, effective {EffectiveAt}",
            notification.SubscriptionId, notification.FromProductHandle, notification.ToProductHandle, notification.EffectiveAt);

        await _emailSender.SendEmailAsync(notification.CustomerReference, "Your subscription plan changed",
            $"Your subscription changed from '{notification.FromProductHandle}' to '{notification.ToProductHandle}', effective {notification.EffectiveAt:yyyy-MM-dd HH:mm zzz}.");
    }
}
