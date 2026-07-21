using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.TestSupport;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Exercises <c>SubscriptionService</c>'s orchestration logic against the provider-agnostic
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Interfaces.IBillingClient"/> seam, in isolation
/// from the wire-level behavior covered by the MaxioBillingClient test classes.
/// </summary>
public class SubscriptionServiceTests
{
    private static Subscription MakeSubscription(int id, SubscriptionStatus status, string planHandle = "eshop-pro") =>
        new(id, "shopper@example.com", planHandle, "Pro Plan", 299.00m, status, null, false);

    private static SubscriptionService CreateService(FakeBillingClient billingClient, FakePublisher? publisher = null) =>
        new(billingClient, publisher ?? new FakePublisher(), new FakeAppLogger<SubscriptionService>());

    [Fact]
    public async Task SubscribeAsync_RejectsAnUnconfiguredPlanHandle_WithoutTouchingTheProviderFurther()
    {
        var billingClient = new FakeBillingClient
        {
            OnListPlans = (_) => Task.FromResult<IReadOnlyList<BillingPlan>>(new List<BillingPlan>
            {
                new("eshop-pro", "Pro Plan", 299.00m, "month", 1, false)
            })
        };
        var service = CreateService(billingClient);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => service.SubscribeAsync("shopper@example.com", "Ada", "Lovelace", "made-up-plan"));

        Assert.DoesNotContain("EnsureCustomerAsync", billingClient.Calls);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsTheExistingActiveSubscription_WithoutCreatingADuplicate()
    {
        var existing = MakeSubscription(9001, SubscriptionStatus.Active);
        var billingClient = new FakeBillingClient
        {
            OnListPlans = (_) => Task.FromResult<IReadOnlyList<BillingPlan>>(new List<BillingPlan> { new("eshop-pro", "Pro Plan", 299.00m, "month", 1, false) }),
            OnEnsureCustomer = (_, _, _, _, _) => Task.CompletedTask,
            OnListCustomerSubscriptions = (_, _) => Task.FromResult<IReadOnlyList<Subscription>>(new List<Subscription> { existing })
        };
        var publisher = new FakePublisher();
        var service = CreateService(billingClient, publisher);

        var result = await service.SubscribeAsync("shopper@example.com", "Ada", "Lovelace", "eshop-pro");

        Assert.Equal(existing.Id, result.Id);
        Assert.DoesNotContain("CreateSubscriptionAsync", billingClient.Calls);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task RecordUsageAsync_RejectsANonPositiveQuantity_BeforeAnyProviderCall()
    {
        var billingClient = new FakeBillingClient();
        var service = CreateService(billingClient);

        await Assert.ThrowsAsync<ArgumentException>(() => service.RecordUsageAsync(1, 0, null));

        Assert.Empty(billingClient.Calls);
    }

    [Fact]
    public async Task CommitPlanChangeAsync_RejectsAStalePreview_AndNeverCommits()
    {
        var billingClient = new FakeBillingClient
        {
            OnListPlans = (_) => Task.FromResult<IReadOnlyList<BillingPlan>>(new List<BillingPlan> { new("basic-plan", "Basic Plan", 29.00m, "month", 1, false) }),
            OnGetSubscription = (_, _) => Task.FromResult(MakeSubscription(9001, SubscriptionStatus.Active)),
            OnPreviewPlanChange = (_, _, _, _) => Task.FromResult(new PlanChangePreview("eshop-pro", "basic-plan", true, 10.00m, 10.00m, 0m, DateTimeOffset.UtcNow)),
            OnCommitPlanChange = (_, _, _, _) => throw new InvalidOperationException("Commit must never be called when the preview is stale.")
        };
        var service = CreateService(billingClient);

        await Assert.ThrowsAsync<PlanChangePreviewStaleException>(
            () => service.CommitPlanChangeAsync(9001, "basic-plan", applyNow: true, expectedProratedAmount: 20.00m));
    }

    [Fact]
    public async Task PauseAsync_RejectsAnAlreadyPausedSubscription_WithoutCallingTheProvider()
    {
        var billingClient = new FakeBillingClient
        {
            OnGetSubscription = (_, _) => Task.FromResult(MakeSubscription(9001, SubscriptionStatus.Paused)),
            OnPause = (_, _) => throw new InvalidOperationException("Pause must never be called for an already-paused subscription.")
        };
        var service = CreateService(billingClient);

        var ex = await Assert.ThrowsAsync<InvalidSubscriptionStateException>(() => service.PauseAsync(9001));

        Assert.Equal(9001, ex.SubscriptionId);
        Assert.Equal("Paused", ex.CurrentState);
    }

    [Fact]
    public async Task Lifecycle_RefreshesAndSurfacesTheConflict_WhenTheProviderRejectsATransitionTheLocalCheckAllowed()
    {
        var callCount = 0;
        var billingClient = new FakeBillingClient
        {
            OnGetSubscription = (_, _) =>
            {
                callCount++;
                // First read: looks pauseable. Second read (after the provider rejects): the
                // subscription had drifted out-of-band to Canceled in the meantime.
                var status = callCount == 1 ? SubscriptionStatus.Active : SubscriptionStatus.Canceled;
                return Task.FromResult(MakeSubscription(9001, status));
            },
            OnPause = (_, _) => throw new BillingProviderException("Cannot hold a canceled subscription", 422)
        };
        var service = CreateService(billingClient);

        var ex = await Assert.ThrowsAsync<InvalidSubscriptionStateException>(() => service.PauseAsync(9001));

        Assert.Equal("Canceled", ex.CurrentState);
        Assert.Equal(2, callCount);
    }
}
