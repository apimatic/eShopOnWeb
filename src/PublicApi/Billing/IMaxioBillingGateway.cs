using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// What a lookup of "the subscription for this reference" may match on.
/// </summary>
/// <param name="Reference">The deterministic subscription reference.</param>
/// <param name="CustomerId">The caller's own provider customer id (fallback list scope).</param>
/// <param name="PlanHandle">The validated plan handle.</param>
/// <param name="UnsettledAttemptStartedAt">
/// Start of a write attempt whose outcome is unknown. When set, a subscription of the same customer to the
/// same plan created at or after this instant (minus a clock-skew allowance) also counts as the match, in
/// case the provider did not persist the reference.
/// </param>
public sealed record SubscriptionLookup(string Reference, int CustomerId, string PlanHandle, DateTimeOffset? UnsettledAttemptStartedAt);

/// <summary>
/// The one boundary in front of the Maxio Advanced Billing SDK. Every SDK call, its total time budget and
/// every SDK <c>catch</c> live behind this interface; callers only ever see provider-neutral models,
/// <see cref="WriteOutcome{T}"/> and <see cref="BillingProviderException"/>.
/// </summary>
public interface IMaxioBillingGateway
{
    /// <summary>Non-archived products of the configured family, paged with a cap (see <see cref="PlanCatalog.Truncated"/>).</summary>
    Task<PlanCatalog> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>The customer with this reference, or <c>null</c> when the provider has none.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>Creates the customer; an unknown outcome is settled by re-reading by reference before returning.</summary>
    Task<WriteOutcome<BillingCustomer>> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken);

    /// <summary>The subscription matching <paramref name="lookup"/>, or <c>null</c> when the provider has none.</summary>
    Task<BillingSubscription?> FindSubscriptionAsync(SubscriptionLookup lookup, CancellationToken cancellationToken);

    /// <summary>Creates the subscription; an unknown outcome is settled by re-reading by reference before returning.</summary>
    Task<WriteOutcome<BillingSubscription>> CreateSubscriptionAsync(NewBillingSubscription subscription, CancellationToken cancellationToken);

    /// <summary>All subscriptions of the given customer.</summary>
    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}
