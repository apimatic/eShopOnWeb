using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

/// <summary>Shared fixtures: a substitute gateway, an in-memory claim store and a hand-driven clock.</summary>
internal sealed class SubscriptionServiceTestContext
{
    public const string BuyerId = "buyer-1";
    public const string Email = "shopper@example.com";
    public const string PlanHandle = "eshop-pro";
    public const int BillingCustomerId = 501;

    public ISubscriptionBillingGateway Gateway { get; } = Substitute.For<ISubscriptionBillingGateway>();
    public InMemoryClaimStore Store { get; } = new();
    public ManualTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
    public IAppLogger<SubscriptionService> Logger { get; } = Substitute.For<IAppLogger<SubscriptionService>>();

    public SubscriptionServiceTestContext()
    {
        Gateway.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns(new SubscriptionPlanCatalog(new[] { new SubscriptionPlan(1, PlanHandle, "Pro Plan", null, 29900, 1, "month") }, false));
    }

    public SubscriptionService CreateService() => new(Gateway, Store, Logger, Clock);

    public static SubscribeCommand Command(string planHandle = PlanHandle) => new(BuyerId, Email, planHandle, null, null);

    public static BillingSubscription Subscription(int id, string reference, string state = "active") =>
        new(id, reference, state, PlanHandle, "Pro Plan", 29900, "USD", 1, "month",
            DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    /// <summary>A customer already recorded for the buyer, so subscribe goes straight to the subscription write.</summary>
    public void WithRecordedCustomer()
    {
        var customer = new BillingCustomer(BuyerId, Clock.GetUtcNow());
        customer.RecordBillingCustomer(BillingCustomerId);
        Store.Customers[BuyerId] = customer;
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Claim store with the same refusal semantics as the EF store: a second insert for a key returns false.</summary>
internal sealed class InMemoryClaimStore : ISubscriptionClaimStore
{
    public Dictionary<(string BuyerId, string PlanHandle), SubscriptionEnrollment> Enrollments { get; } = new();
    public Dictionary<string, BillingCustomer> Customers { get; } = new();

    public Task<bool> TryClaimEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default) =>
        Task.FromResult(Enrollments.TryAdd((enrollment.BuyerId, enrollment.PlanHandle), enrollment));

    public Task<SubscriptionEnrollment?> GetEnrollmentAsync(string buyerId, string planHandle, CancellationToken cancellationToken = default) =>
        Task.FromResult(Enrollments.GetValueOrDefault((buyerId, planHandle)));

    public Task<IReadOnlyList<SubscriptionEnrollment>> ListEnrollmentsAsync(string buyerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionEnrollment>>(Enrollments.Values.Where(e => e.BuyerId == buyerId).ToList());

    public Task UpdateEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ReleaseEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        Enrollments.Remove((enrollment.BuyerId, enrollment.PlanHandle));
        return Task.CompletedTask;
    }

    public Task<bool> TryClaimCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default) =>
        Task.FromResult(Customers.TryAdd(customer.BuyerId, customer));

    public Task<BillingCustomer?> GetCustomerAsync(string buyerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Customers.GetValueOrDefault(buyerId));

    public Task UpdateCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ReleaseCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default)
    {
        Customers.Remove(customer.BuyerId);
        return Task.CompletedTask;
    }
}
