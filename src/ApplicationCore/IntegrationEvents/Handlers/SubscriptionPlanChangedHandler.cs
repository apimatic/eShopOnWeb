using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>Best-effort audit log entry for a plan change. Never throws (§2.5: best-effort eventing).</summary>
public class SubscriptionPlanChangedHandler : INotificationHandler<SubscriptionPlanChanged>
{
    private readonly IAppLogger<SubscriptionPlanChangedHandler> _logger;

    public SubscriptionPlanChangedHandler(IAppLogger<SubscriptionPlanChangedHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Subscription {0} for {1} moved from {2} to {3}, effective {4}",
                notification.SubscriptionId, notification.CustomerReference,
                notification.FromPlanHandle, notification.ToPlanHandle, notification.EffectiveDate);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SubscriptionPlanChanged handler failed for subscription {0}: {1}", notification.SubscriptionId, ex.Message);
        }

        return Task.CompletedTask;
    }
}
