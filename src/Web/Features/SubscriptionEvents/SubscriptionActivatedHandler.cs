using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.SubscriptionEvents;

/// <summary>
/// In-process reaction to UC1 enrollment (§2.5): sends a confirmation email and writes an audit
/// log entry. Runs best-effort — <see cref="ApplicationCore.Services.SubscriptionService"/> already
/// swallows and logs any failure raised here.
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
        _logger.LogInformation(
            "Subscription {0} activated on plan '{1}' for user {2}.",
            notification.SubscriptionId, notification.ProductHandle, notification.UserReference);

        await _emailSender.SendEmailAsync(
            notification.UserReference,
            "Your eShopOnWeb subscription is active",
            $"You are now subscribed to plan '{notification.ProductHandle}' (subscription #{notification.SubscriptionId}).");
    }
}
