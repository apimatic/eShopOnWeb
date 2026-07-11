using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;

public class SubscriptionPlanChangedLoggingHandler : INotificationHandler<SubscriptionPlanChanged>
{
    private readonly IAppLogger<SubscriptionPlanChangedLoggingHandler> _logger;

    public SubscriptionPlanChangedLoggingHandler(IAppLogger<SubscriptionPlanChangedLoggingHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SubscriptionPlanChanged notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscription {0} for {1} changed plan {2} -> {3} ({4})",
            notification.SubscriptionId, notification.BuyerId, notification.PreviousProductHandle,
            notification.NewProductHandle, notification.Immediate ? "immediate" : "at renewal");
        return Task.CompletedTask;
    }
}
