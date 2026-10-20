using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<PlanList> ListPlansAsync(Guid correlationId, CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(string userName, string? planHandle, Guid correlationId, CancellationToken cancellationToken);

    Task<System.Collections.Generic.IReadOnlyList<SubscriptionDto>> ListMineAsync(string userName, Guid correlationId, CancellationToken cancellationToken);
}
