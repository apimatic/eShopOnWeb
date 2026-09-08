using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Application-facing contract for the Maxio Advanced Billing subscription capability.
/// Implementations translate Maxio/SDK failures into <see cref="MaxioException"/> subtypes.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>Lists the active plans in the configured Maxio product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the profile and enrolls them in the plan.
    /// Idempotent: when the customer and/or an active subscription already exists they are
    /// returned instead of creating duplicates.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions of the customer identified by <paramref name="customerReference"/>.
    /// Never creates a customer; when none exists an empty list is returned.
    /// </summary>
    Task<IReadOnlyList<SubscriptionRecord>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
