using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

// Best-effort, in-process reaction to a new subscription (see §2.5 of the integration plan).
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
        await _emailSender.SendEmailAsync(notification.CustomerReference, "Your eShopOnWeb subscription is active",
            $"You're subscribed to plan '{notification.PlanHandle}' (subscription #{notification.SubscriptionId}).");

        _logger.LogInformation("Subscription {0} activated for customer {1} on plan {2}.",
            notification.SubscriptionId, notification.CustomerReference, notification.PlanHandle);
    }
}
