using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Recurring subscriptions; the billing system of record is Maxio. Failures surface as <c>SubscriptionException</c>.</summary>
public interface ISubscriptionService
{
    Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionSummary>> ListMineAsync(string userName, CancellationToken cancellationToken);
}
