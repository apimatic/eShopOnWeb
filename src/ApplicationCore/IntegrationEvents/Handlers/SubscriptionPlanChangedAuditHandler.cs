using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>Writes an audit log entry when a subscription's plan changes.</summary>
public class SubscriptionPlanChangedAuditHandler : INotificationHandler<SubscriptionPlanChanged>
{
    private readonly IAppLogger<SubscriptionPlanChangedAuditHandler> _logger;

    public SubscriptionPlanChangedAuditHandler(IAppLogger<SubscriptionPlanChangedAuditHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {SubscriptionId} for {UserName} changed plan {OldProductHandle} -> {NewProductHandle} ({Timing}), effective {EffectiveAt}",
            notification.SubscriptionId, notification.UserName, notification.OldProductHandle, notification.NewProductHandle,
            notification.AppliedImmediately ? "immediate" : "at next renewal", notification.EffectiveAt);

        return Task.CompletedTask;
    }
}
