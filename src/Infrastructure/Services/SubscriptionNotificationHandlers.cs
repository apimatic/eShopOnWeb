using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Infrastructure.Services;

// Best-effort, in-process reactions to subscription lifecycle notifications (§2.5): an audit
// log entry via the existing IAppLogger<>, mirroring how EmailSender/LoggerAdapter already
// live in Infrastructure behind ApplicationCore interfaces.
public class SubscriptionActivatedHandler : INotificationHandler<SubscriptionActivated>
{
    private readonly IAppLogger<SubscriptionActivatedHandler> _logger;
    private readonly IEmailSender _emailSender;

    public SubscriptionActivatedHandler(IAppLogger<SubscriptionActivatedHandler> logger, IEmailSender emailSender)
    {
        _logger = logger;
        _emailSender = emailSender;
    }

    public async Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} activated for {1} on plan {2} (${3} cents).",
            notification.SubscriptionId, notification.UserReference, notification.ProductHandle, notification.PriceInCents);

        await _emailSender.SendEmailAsync(notification.UserReference,
            "Your eShopOnWeb subscription is active",
            $"You're subscribed to {notification.ProductName} at {notification.PriceInCents / 100m:C}/month.");
    }
}

public class SubscriptionPlanChangedHandler : INotificationHandler<SubscriptionPlanChanged>
{
    private readonly IAppLogger<SubscriptionPlanChangedHandler> _logger;

    public SubscriptionPlanChangedHandler(IAppLogger<SubscriptionPlanChangedHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} for {1} changed plan {2} -> {3} (appliedNow: {4}).",
            notification.SubscriptionId, notification.UserReference, notification.OldProductHandle, notification.NewProductHandle, notification.AppliedNow);
        return Task.CompletedTask;
    }
}

public class SubscriptionStateChangedHandler : INotificationHandler<SubscriptionStateChanged>
{
    private readonly IAppLogger<SubscriptionStateChangedHandler> _logger;

    public SubscriptionStateChangedHandler(IAppLogger<SubscriptionStateChangedHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} for {1} changed state {2} -> {3}.",
            notification.SubscriptionId, notification.UserReference, notification.OldState, notification.NewState);
        return Task.CompletedTask;
    }
}
