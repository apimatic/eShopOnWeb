using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Read access to the recurring plans a shopper may subscribe to.
/// </summary>
public interface ISubscriptionPlanCatalog
{
    /// <summary>
    /// Lists the plans available for subscription, ordered by recurring price ascending.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a single plan by its stable handle, or <c>null</c> when no such plan is available.
    /// </summary>
    Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default);
}
