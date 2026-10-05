using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

/// <summary>A dictionary-backed claim store that refuses a second insert for the same key, like a primary key does.</summary>
public class FakeSubscriptionStore : ISubscriptionStore
{
    public Dictionary<string, BillingCustomer> Customers { get; } = new();
    public Dictionary<string, SubscriptionEnrollment> Enrollments { get; } = new();

    public Task<BillingCustomer?> GetCustomerAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult(Customers.GetValueOrDefault(userId));

    public Task<bool> TryClaimCustomerAsync(BillingCustomer claim, CancellationToken cancellationToken) =>
        Task.FromResult(Customers.TryAdd(claim.UserId, claim));

    public Task<bool> TryRenewCustomerClaimAsync(BillingCustomer claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        claim.Renew(now);
        return Task.FromResult(true);
    }

    public Task<bool> SaveCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task ReleaseCustomerClaimAsync(BillingCustomer claim, CancellationToken cancellationToken)
    {
        Customers.Remove(claim.UserId);
        return Task.CompletedTask;
    }

    public Task<SubscriptionEnrollment?> GetEnrollmentAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(Enrollments.GetValueOrDefault(id));

    public Task<IReadOnlyList<SubscriptionEnrollment>> ListEnrollmentsAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionEnrollment>>(Enrollments.Values.Where(e => e.UserId == userId).ToList());

    public Task<bool> TryClaimEnrollmentAsync(SubscriptionEnrollment claim, CancellationToken cancellationToken) =>
        Task.FromResult(Enrollments.TryAdd(claim.Id, claim));

    public Task<bool> TryRenewEnrollmentClaimAsync(SubscriptionEnrollment claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        claim.Renew(now);
        return Task.FromResult(true);
    }

    public Task<bool> SaveEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task ReleaseEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        Enrollments.Remove(enrollment.Id);
        return Task.CompletedTask;
    }
}
