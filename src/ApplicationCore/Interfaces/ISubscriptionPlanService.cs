using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Read access to the recurring plans offered by the billing system of record.
/// </summary>
public interface ISubscriptionPlanService
{
    /// <summary>
    /// Lists the plans a shopper may subscribe to, ordered by price. Archived plans are excluded.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a plan by its stable handle, or <c>null</c> when no such plan is offered.
    /// </summary>
    Task<SubscriptionPlan?> FindPlanAsync(string handle, CancellationToken cancellationToken = default);
}
