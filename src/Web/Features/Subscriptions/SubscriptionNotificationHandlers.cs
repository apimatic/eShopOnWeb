using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// In-process reactions to subscription lifecycle notifications (§2.5): an audit log line via
/// <see cref="IAppLogger{T}"/> plus a best-effort confirmation email via the existing
/// <see cref="IEmailSender"/>. These mirror how the storefront already reacts to events in-process.
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
        var s = notification.Subscription;
        _logger.LogInformation($"Subscription {s.Id} activated for {notification.UserName} on plan {s.ProductHandle} (${s.ProductPrice}/{s.Interval}).");
        await _emailSender.SendEmailAsync(notification.UserName, "Your subscription is active",
            $"You're subscribed to {s.ProductName} at ${s.ProductPrice}/{s.Interval}. Next billing: {s.CurrentPeriodEndsAt:d}.");
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
        _logger.LogInformation($"Subscription {notification.SubscriptionId} changed plan {notification.OldProductHandle} -> {notification.NewProductHandle}.");
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
        _logger.LogInformation($"Subscription {notification.SubscriptionId} state {notification.OldState} -> {notification.NewState}.");
        return Task.CompletedTask;
    }
}
