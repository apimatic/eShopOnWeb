using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// In-process reaction to a lifecycle transition (UC4 - pause/resume/cancel/reactivate): sends a
/// confirmation email and writes an audit log entry. Best-effort - see plan.md §2.5.
/// </summary>
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
        _logger.LogInformation("Subscription {0} moved from {1} to {2} for {3}",
            notification.SubscriptionId, notification.OldState, notification.NewState, notification.CustomerReference);

        await _emailSender.SendEmailAsync(
            notification.CustomerReference,
            "Your subscription status changed",
            $"Your subscription is now {notification.NewState} (was {notification.OldState}).");
    }
}
