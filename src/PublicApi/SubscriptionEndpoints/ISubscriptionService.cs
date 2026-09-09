using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Orchestrates the Subscribe capability: resolves the plan catalog from
/// Maxio, guarantees a Maxio customer exists for the eShopOnWeb user
/// (idempotently), enrolls them in the chosen plan, and reports the resulting
/// subscription state back. Maxio is the billing system of record.
/// </summary>
public interface ISubscriptionService
{
    Task<ListSubscriptionPlansResponse> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <exception cref="Maxio.MaxioPlanNotFoundException">The requested plan does not exist.</exception>
    /// <exception cref="Maxio.MaxioConfigurationException">No default plan can be resolved.</exception>
    Task<SubscriptionDetailsDto> SubscribeAsync(string userId, string email, string firstName, string lastName,
        string? planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDetailsDto>> GetMySubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}
