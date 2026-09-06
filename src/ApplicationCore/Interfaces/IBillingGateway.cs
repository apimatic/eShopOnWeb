using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port onto the external billing system of record. The adapter owns every provider-side identifier
/// (customer references, subscription references) so the domain never has to know how they are shaped.
/// </summary>
public interface IBillingGateway
{
    /// <summary>Handle of the product family whose products are offered as plans.</summary>
    string ProductFamilyHandle { get; }

    /// <summary>Plan configured as the subscribe target when the caller does not name one. Optional.</summary>
    string? DefaultPlanHandle { get; }

    /// <summary>Every non-archived plan available for signup, cheapest first.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>The provider customer for this user, or null if they have never been enrolled.</summary>
    Task<BillingCustomer?> FindCustomerAsync(string userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider customer for the user, creating it if it does not exist yet.
    /// Idempotent: repeated calls (including concurrent ones) resolve to the same customer.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(SubscriberProfile subscriber, CancellationToken cancellationToken = default);

    /// <summary>All subscriptions attached to a provider customer.</summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up the subscription previously created for this user/idempotency-key pair, or null if
    /// there is none.
    /// </summary>
    Task<CustomerSubscription?> FindSubscriptionAsync(string userKey, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the customer on a plan, stamping the subscription with a reference derived from
    /// <paramref name="userKey"/> and <paramref name="idempotencyKey"/> so the signup can be found again.
    /// </summary>
    Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string userKey, string idempotencyKey, CancellationToken cancellationToken = default);
}
