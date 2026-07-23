using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Confirms a lifecycle transition to the customer and records it in the audit log.
/// </summary>
public class SubscriptionStateChangedHandler : INotificationHandler<SubscriptionStateChanged>
{
    private readonly IEmailSender _emailSender;
    private readonly IAppLogger<SubscriptionStateChangedHandler> _logger;

    public SubscriptionStateChangedHandler(
        IEmailSender emailSender,
        IAppLogger<SubscriptionStateChangedHandler> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {SubscriptionId} transitioned {PreviousState} -> {NewState} via {Action}.",
            notification.Subscription.Id,
            notification.PreviousState,
            notification.NewState,
            notification.Action);

        await _emailSender.SendEmailAsync(
            notification.UserReference,
            $"Your eShopOnWeb subscription was {notification.Action.ToString().ToLowerInvariant()}d",
            $"Your subscription is now {notification.NewState}, effective {notification.EffectiveAt:g}.");
    }
}
