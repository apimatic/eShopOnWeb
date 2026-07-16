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
        _logger.LogInformation("Subscription {0} for user {1} changed plan {2} -> {3}, effective {4}.",
            notification.SubscriptionId, notification.UserId, notification.OldProductHandle, notification.NewProductHandle, notification.EffectiveAt);

        await _emailSender.SendEmailAsync(notification.UserId, "Your eShopOnWeb subscription plan changed",
            $"Your subscription moved from {notification.OldProductHandle} to {notification.NewProductHandle}, effective {notification.EffectiveAt:d}.");
    }
}
