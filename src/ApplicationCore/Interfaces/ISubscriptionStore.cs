using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Persists the claims that keep billing writes single. The TryClaim methods insert a row whose primary key
/// identifies the write and return false when the store refuses it because that row already exists.
/// The TryRenew methods take over a stale claim, and the Save methods persist changes to a claim; both return
/// false when another request changed that claim first.
/// </summary>
public interface ISubscriptionStore
{
    Task<BillingCustomer?> GetCustomerAsync(string userId, CancellationToken cancellationToken);
    Task<bool> TryClaimCustomerAsync(BillingCustomer claim, CancellationToken cancellationToken);
    Task<bool> TryRenewCustomerClaimAsync(BillingCustomer claim, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> SaveCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken);
    Task ReleaseCustomerClaimAsync(BillingCustomer claim, CancellationToken cancellationToken);

    Task<SubscriptionEnrollment?> GetEnrollmentAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionEnrollment>> ListEnrollmentsAsync(string userId, CancellationToken cancellationToken);
    Task<bool> TryClaimEnrollmentAsync(SubscriptionEnrollment claim, CancellationToken cancellationToken);
    Task<bool> TryRenewEnrollmentClaimAsync(SubscriptionEnrollment claim, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> SaveEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken);
    Task ReleaseEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken);
}
