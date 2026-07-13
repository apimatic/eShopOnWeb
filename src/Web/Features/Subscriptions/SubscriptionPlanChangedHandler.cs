using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// In-process reaction to a committed plan change (UC3): sends a confirmation email and writes
/// an audit log entry. Best-effort - see plan.md §2.5.
/// </summary>
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
        _logger.LogInformation("Subscription {0} moved from {1} to {2} for {3}",
            notification.SubscriptionId, notification.OldProductHandle, notification.NewProductHandle, notification.CustomerReference);

        await _emailSender.SendEmailAsync(
            notification.CustomerReference,
            "Your plan has changed",
            $"Your subscription moved from {notification.OldProductHandle} to {notification.NewProductHandle}.");
    }
}
