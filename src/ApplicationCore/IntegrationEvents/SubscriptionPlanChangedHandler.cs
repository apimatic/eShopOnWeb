using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

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
            "Subscription {0} for {1} changed plan {2} -> {3}, prorated amount {4} cents, effective {5}",
            notification.SubscriptionId, notification.BuyerId, notification.OldProductHandle,
            notification.NewProductHandle, notification.ProratedAmountInCents, notification.EffectiveAt);

        return Task.CompletedTask;
    }
}
