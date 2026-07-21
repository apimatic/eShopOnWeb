using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>Best-effort audit log entry for a lifecycle transition. Never throws (§2.5: best-effort eventing).</summary>
public class SubscriptionStateChangedHandler : INotificationHandler<SubscriptionStateChanged>
{
    private readonly IAppLogger<SubscriptionStateChangedHandler> _logger;

    public SubscriptionStateChangedHandler(IAppLogger<SubscriptionStateChangedHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Subscription {0} for {1} changed state from {2} to {3}",
                notification.SubscriptionId, notification.CustomerReference, notification.OldState, notification.NewState);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SubscriptionStateChanged handler failed for subscription {0}: {1}", notification.SubscriptionId, ex.Message);
        }

        return Task.CompletedTask;
    }
}
