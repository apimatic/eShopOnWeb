using MediatR;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Writes an audit trail when a customer is enrolled in a plan, and confirms it by email.
/// </summary>
public class SubscriptionActivatedHandler : INotificationHandler<SubscriptionActivated>
{
    private readonly IEmailSender _emailSender;
    private readonly IAppLogger<SubscriptionActivatedHandler> _logger;

    public SubscriptionActivatedHandler(IEmailSender emailSender,
        IAppLogger<SubscriptionActivatedHandler> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task Handle(SubscriptionActivated notification, CancellationToken cancellationToken)
    {
        var subscription = notification.Subscription;

        _logger.LogInformation(
            "Subscription {0} activated for {1} on plan {2} at {3:C}; next billing {4}.",
            subscription.Id,
            notification.UserReference,
            subscription.PlanHandle ?? "unknown",
            subscription.PlanPrice ?? 0m,
            subscription.NextBillingDate?.ToString("u") ?? "unscheduled");

        await _emailSender.SendEmailAsync(notification.UserReference,
            "Your eShopOnWeb subscription is active",
            $"You are now subscribed to {subscription.PlanName ?? subscription.PlanHandle}. " +
            $"Your next billing date is {subscription.NextBillingDate?.ToString("d") ?? "not yet scheduled"}.");
    }
}

/// <summary>Writes an audit trail when a subscription moves to a different plan.</summary>
public class SubscriptionPlanChangedHandler : INotificationHandler<SubscriptionPlanChanged>
{
    private readonly IAppLogger<SubscriptionPlanChangedHandler> _logger;

    public SubscriptionPlanChangedHandler(IAppLogger<SubscriptionPlanChangedHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {0} for {1} changed plan {2} -> {3} ({4}); payment due {5:C}.",
            notification.SubscriptionId,
            notification.UserReference,
            notification.PreviousPlanHandle ?? "unknown",
            notification.NewPlanHandle,
            notification.Timing,
            notification.AppliedPreview.PaymentDue);

        return Task.CompletedTask;
    }
}

/// <summary>Writes an audit trail for every lifecycle transition.</summary>
public class SubscriptionStateChangedHandler : INotificationHandler<SubscriptionStateChanged>
{
    private readonly IAppLogger<SubscriptionStateChangedHandler> _logger;

    public SubscriptionStateChangedHandler(IAppLogger<SubscriptionStateChangedHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {0} for {1}: {2} moved state {3} -> {4}.",
            notification.SubscriptionId,
            notification.UserReference,
            notification.Action,
            notification.PreviousStatus,
            notification.NewStatus);

        return Task.CompletedTask;
    }
}
