using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing.Contracts;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<IReadOnlyList<MaxioPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionProvisionResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForUserAsync(string userKey, CancellationToken cancellationToken = default);
}
