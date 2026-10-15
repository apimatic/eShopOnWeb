using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

/// <summary>Shared fixture: a substitute billing gateway and an in-memory claim store with insert-or-fail semantics.</summary>
public class SubscriptionServiceTestContext
{
    public const string Family = "eshop-subscribe";
    public static readonly Subscriber Shopper = new("demouser@microsoft.com", "demouser@microsoft.com");

    public ISubscriptionBillingGateway Gateway { get; } = Substitute.For<ISubscriptionBillingGateway>();
    public FakeEnrollmentStore Store { get; } = new();
    public TestTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    public SubscriptionServiceTestContext()
    {
        Gateway.ProductFamilyHandle.Returns(Family);
        Gateway.GetPlansAsync(Arg.Any<CancellationToken>()).Returns(new PlanCatalog(Family, new[]
        {
            new BillingPlan(1, "eshop-pro", "Pro Plan", null, 29900, 1, "month"),
            new BillingPlan(2, "basic-plan", "Basic Plan", null, 2900, 1, "month"),
        }, false));
        Gateway.EnsureCustomerAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>()).Returns(77);
        Gateway.ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
    }

    public SubscriptionService CreateService() =>
        new(Gateway, Store, Substitute.For<IAppLogger<SubscriptionService>>(), Clock);

    public static BillingSubscription Subscription(int id, string reference, string plan = "eshop-pro", string state = "active") =>
        new(id, reference, 77, state, plan, "Plan", Family, 29900, 29900, "USD",
            new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero), null, null, new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero));

    public string ClaimKey => SubscriptionEnrollment.CreateClaimKey(SubscriptionService.CustomerReferenceFor(Shopper.UserName), Family);
}

public sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _now;
    public TestTimeProvider(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

public sealed class FakeEnrollmentStore : ISubscriptionEnrollmentStore
{
    public Dictionary<string, SubscriptionEnrollment> Rows { get; } = new();
    public int Releases { get; private set; }

    public Task<SubscriptionEnrollment?> TryClaimAsync(SubscriptionEnrollment claim, CancellationToken cancellationToken = default)
    {
        lock (Rows)
        {
            if (Rows.TryGetValue(claim.ClaimKey, out var existing))
            {
                return Task.FromResult<SubscriptionEnrollment?>(existing);
            }
            Rows[claim.ClaimKey] = claim;
            return Task.FromResult<SubscriptionEnrollment?>(null);
        }
    }

    public Task<SubscriptionEnrollment?> GetAsync(string claimKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.TryGetValue(claimKey, out var e) ? e : null);

    public Task<bool> TryUpdateAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.ContainsKey(enrollment.ClaimKey));

    public Task ReleaseAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        Releases++;
        Rows.Remove(enrollment.ClaimKey);
        return Task.CompletedTask;
    }
}
