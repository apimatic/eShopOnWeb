using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Fronts the Maxio Advanced Billing subscription-billing capability. Implementations own
/// find-or-create customer idempotency and duplicate-subscription prevention.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken ct = default);

    Task<SubscriptionDetails> SubscribeAsync(SubscriptionEnrollmentRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken ct = default);
}
