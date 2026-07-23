using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Features.Subscriptions;

/// <summary>
/// Confirms a plan change to the customer and records it in the audit log.
/// </summary>
public class SubscriptionPlanChangedHandler : INotificationHandler<SubscriptionPlanChanged>
{
    private readonly IEmailSender _emailSender;
    private readonly IAppLogger<SubscriptionPlanChangedHandler> _logger;

    public SubscriptionPlanChangedHandler(
        IEmailSender emailSender,
        IAppLogger<SubscriptionPlanChangedHandler> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {SubscriptionId} moved from {PreviousPlan} to {NewPlan} ({Timing}).",
            notification.Subscription.Id,
            notification.PreviousPlanHandle,
            notification.NewPlanHandle,
            notification.Timing);

        var when = notification.Timing == PlanChangeTiming.Immediate
            ? "immediately"
            : $"at your next renewal on {notification.EffectiveAt:d}";

        await _emailSender.SendEmailAsync(
            notification.UserReference,
            "Your eShopOnWeb plan has changed",
            $"Your subscription moved from {notification.PreviousPlanHandle} to {notification.NewPlanHandle} {when}. " +
            $"Amount due for this change: {notification.ProrationAmount:C}.");
    }
}
