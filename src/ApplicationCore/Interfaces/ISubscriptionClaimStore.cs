using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Durable claims that keep a buyer's billing writes single. A claim is an insert keyed by what the request is for;
/// the store refuses a second insert for the same key, which is what stops a double-submit before it reaches the
/// billing system.
/// </summary>
public interface ISubscriptionClaimStore
{
    /// <summary>Inserts the claim; false when one with the same buyer id already exists.</summary>
    Task<bool> TryClaimCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default);

    Task<BillingCustomer?> GetCustomerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task UpdateCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default);

    Task ReleaseCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>Inserts the claim; false when one with the same claim key already exists.</summary>
    Task<bool> TryClaimSubscriptionAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);

    Task<SubscriptionEnrollment?> GetEnrollmentAsync(string claimKey, CancellationToken cancellationToken = default);

    Task<int> CountEndedEnrollmentsAsync(string buyerId, string productFamilyHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionEnrollment>> ListEnrollmentsAsync(string buyerId, string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>Enrollments that are <see cref="EnrollmentStatus.Unknown"/>, or <see cref="EnrollmentStatus.Pending"/> and claimed before <paramref name="pendingClaimedBefore"/>.</summary>
    Task<IReadOnlyList<SubscriptionEnrollment>> ListUnsettledEnrollmentsAsync(DateTimeOffset pendingClaimedBefore, int max, CancellationToken cancellationToken = default);

    Task UpdateEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);

    Task ReleaseEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);
}
