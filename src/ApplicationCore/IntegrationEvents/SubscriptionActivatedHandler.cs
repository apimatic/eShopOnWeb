using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// In-process reaction to UC1's activation (plan.md §2.5) — sends a confirmation email and writes an
/// audit log entry. Best-effort: <see cref="Services.SubscriptionService"/> already catches and logs
/// any failure from publishing, so a failure here never rolls back the subscription.
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
            "Subscription {SubscriptionId} activated for user {UserId} on plan {ProductHandle}.",
            notification.SubscriptionId, notification.UserId, notification.ProductHandle);

        await _emailSender.SendEmailAsync(
            notification.UserId,
            "Your eShopOnWeb subscription is active",
            $"You're subscribed to plan '{notification.ProductHandle}' (subscription #{notification.SubscriptionId}).");
    }
}
