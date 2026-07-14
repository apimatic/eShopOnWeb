using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

/// <summary>Writes an audit log entry when a subscription's lifecycle state changes.</summary>
public class SubscriptionStateChangedAuditHandler : INotificationHandler<SubscriptionStateChanged>
{
    private readonly IAppLogger<SubscriptionStateChangedAuditHandler> _logger;

    public SubscriptionStateChangedAuditHandler(IAppLogger<SubscriptionStateChangedAuditHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionStateChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Subscription {SubscriptionId} for {UserName} transitioned {OldState} -> {NewState}, effective {EffectiveAt}",
            notification.SubscriptionId, notification.UserName, notification.OldState, notification.NewState, notification.EffectiveAt);

        return Task.CompletedTask;
    }
}
