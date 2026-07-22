using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Tests the provider-agnostic seam's orchestration (SubscriptionService over IBillingClient):
/// idempotency, input/state validation, stale-preview rejection, and best-effort MediatR eventing.
/// </summary>
public class SubscriptionServiceSeamTests
{
    private readonly FakeBillingClient _billing = new();
    private readonly RecordingPublisher _publisher = new();

    private SubscriptionService CreateService()
        => new(_billing, _publisher, new NullAppLogger<SubscriptionService>());

    // ---------------- UC1 Subscribe ----------------

    [Fact]
    public async Task Subscribe_NewUser_CreatesCustomerAndSubscription_PublishesActivated()
    {
        _billing.OnFindCustomer = _ => null;                 // no existing customer
        _billing.OnCreateCustomer = (r, e) => new BillingCustomer(42, r, e);
        _billing.OnListCustomerSubscriptions = _ => Array.Empty<CustomerSubscription>();
        _billing.OnCreateSubscription = (cid, handle) => Fake.Subscription(500, "active", handle, cid);

        var result = await CreateService().SubscribeAsync("demouser@microsoft.com", "eshop-pro");

        Assert.Equal(500, result.Id);
        Assert.Contains("CreateCustomer", _billing.Calls);
        Assert.Contains("CreateSubscription", _billing.Calls);
        var activated = Assert.IsType<SubscriptionActivated>(Assert.Single(_publisher.Published));
        Assert.Equal("demouser@microsoft.com", activated.UserName);
        Assert.Equal(500, activated.Subscription.Id);
    }

    [Fact]
    public async Task Subscribe_ExistingActiveOnSamePlan_ReturnsExisting_DoesNotCreateDuplicate()
    {
        _billing.OnFindCustomer = r => new BillingCustomer(42, r, r);
        _billing.OnListCustomerSubscriptions = _ => new[] { Fake.Subscription(900, "active", "eshop-pro", 42) };

        var result = await CreateService().SubscribeAsync("demouser@microsoft.com", "eshop-pro");

        Assert.Equal(900, result.Id);                        // returned the existing subscription
        Assert.DoesNotContain("CreateSubscription", _billing.Calls);   // never created a second
        Assert.Empty(_publisher.Published);                  // no activation for a pre-existing sub
    }

    [Fact]
    public async Task Subscribe_NotificationHandlerThrows_SubscriptionStillStands()
    {
        _billing.OnFindCustomer = r => new BillingCustomer(42, r, r);
        _billing.OnCreateSubscription = (cid, handle) => Fake.Subscription(501, "active", handle, cid);
        _publisher.ThrowOnPublish = true;                    // best-effort eventing (§2.5)

        var result = await CreateService().SubscribeAsync("demouser@microsoft.com", "eshop-pro");

        Assert.Equal(501, result.Id);                        // provider action not rolled back
    }

    [Fact]
    public async Task GetSubscriptionsForUser_NoCustomer_ReturnsEmpty()
    {
        _billing.OnFindCustomer = _ => null;

        var result = await CreateService().GetSubscriptionsForUserAsync("ghost@example.com");

        Assert.Empty(result);
        Assert.DoesNotContain("ListCustomerSubscriptions", _billing.Calls);
    }

    // ---------------- UC2 Usage ----------------

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task RecordUsage_NonPositiveQuantity_RejectedBeforeAnyProviderCall(int quantity)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => CreateService().RecordUsageAsync(100, quantity, "bad"));

        Assert.Empty(_billing.Calls);                        // nothing sent to the provider
    }

    [Fact]
    public async Task RecordUsage_NonMeteredComponent_ThrowsConfigurationException()
    {
        _billing.OnGetMeteredComponent = () => new MeteredComponentInfo(1, "api-call", "quantity_based_component", 0.01m);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => CreateService().RecordUsageAsync(100, 5, "x"));

        Assert.DoesNotContain("RecordUsage", _billing.Calls);
    }

    [Fact]
    public async Task RecordUsage_InactiveSubscription_Rejected()
    {
        _billing.OnGetSubscription = id => Fake.Subscription(id, "canceled", "eshop-pro");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => CreateService().RecordUsageAsync(100, 5, "x"));

        Assert.Contains("canceled", ex.Message);
        Assert.DoesNotContain("RecordUsage", _billing.Calls);
    }

    [Fact]
    public async Task RecordUsage_Happy_ReturnsBalanceAndEstimatedCharge()
    {
        _billing.OnGetMeteredComponent = () => new MeteredComponentInfo(3057195, "api-call", "metered_component", 0.01m);
        _billing.OnGetSubscription = id => Fake.Subscription(id, "active", "eshop-pro");
        _billing.OnRecordUsage = (_, q, _) => q;
        _billing.OnGetUsageBalance = _ => 42m;

        var result = await CreateService().RecordUsageAsync(100, 5, "order placed");

        Assert.Equal(5, result.RecordedQuantity);
        Assert.Equal(42m, result.PeriodToDateTotal);
        Assert.Equal(0.01m, result.UnitPrice);
        Assert.Equal(0.42m, result.EstimatedPeriodCharge);   // 42 units * $0.01
    }

    [Fact]
    public async Task RecordUsage_BalanceReadBackFails_UsageStands_TotalUnavailable()
    {
        _billing.OnGetUsageBalance = _ => throw new BillingProviderException("read-back timed out");

        var result = await CreateService().RecordUsageAsync(100, 7, "x");

        Assert.Equal(7, result.RecordedQuantity);            // usage still recorded
        Assert.Null(result.PeriodToDateTotal);               // total marked unavailable, not a failure
    }

    // ---------------- UC3 Plan change ----------------

    [Fact]
    public async Task ChangePlan_StalePreview_RejectsCommit()
    {
        _billing.OnGetSubscription = id => Fake.Subscription(id, "active", "basic-plan");
        // fresh preview differs from the confirmed one -> stale
        _billing.OnPreviewPlanChange = (_, handle, immediate) => new PlanChangePreview(handle, immediate, 0m, 55m, 55m, 0m);
        var confirmed = new PlanChangePreview("eshop-pro", true, 0m, 50m, 50m, 0m);

        await Assert.ThrowsAsync<BillingProviderException>(
            () => CreateService().ChangePlanAsync(100, "eshop-pro", true, confirmed));

        Assert.DoesNotContain("ChangePlan", _billing.Calls);
    }

    [Fact]
    public async Task ChangePlan_SamePlan_RejectedAsNoOp()
    {
        _billing.OnGetSubscription = id => Fake.Subscription(id, "active", "eshop-pro");
        var confirmed = new PlanChangePreview("eshop-pro", true, 0m, 50m, 50m, 0m);

        await Assert.ThrowsAsync<BillingProviderException>(
            () => CreateService().ChangePlanAsync(100, "eshop-pro", true, confirmed));

        Assert.DoesNotContain("PreviewPlanChange", _billing.Calls);
    }

    [Fact]
    public async Task ChangePlan_Valid_CommitsAndPublishesPlanChanged()
    {
        _billing.OnGetSubscription = id => Fake.Subscription(id, "active", "basic-plan");
        _billing.OnPreviewPlanChange = (_, handle, immediate) => new PlanChangePreview(handle, immediate, 0m, 50m, 50m, 0m);
        _billing.OnChangePlan = (id, handle, _) => Fake.Subscription(id, "active", handle);
        var confirmed = new PlanChangePreview("eshop-pro", true, 0m, 50m, 50m, 0m);

        var result = await CreateService().ChangePlanAsync(100, "eshop-pro", true, confirmed);

        Assert.Equal("eshop-pro", result.ProductHandle);
        var evt = Assert.IsType<SubscriptionPlanChanged>(Assert.Single(_publisher.Published));
        Assert.Equal("basic-plan", evt.OldProductHandle);
        Assert.Equal("eshop-pro", evt.NewProductHandle);
    }

    // ---------------- UC4 Lifecycle ----------------

    [Fact]
    public async Task Pause_WhenNotActive_RejectedBeforeProviderCall()
    {
        _billing.OnGetSubscription = id => Fake.Subscription(id, "on_hold", "eshop-pro");

        await Assert.ThrowsAsync<BillingProviderException>(() => CreateService().PauseAsync(100));

        Assert.DoesNotContain("Pause", _billing.Calls);
    }

    [Fact]
    public async Task Resume_WhenNotOnHold_Rejected()
    {
        _billing.OnGetSubscription = id => Fake.Subscription(id, "active", "eshop-pro");

        await Assert.ThrowsAsync<BillingProviderException>(() => CreateService().ResumeAsync(100));
        Assert.DoesNotContain("Resume", _billing.Calls);
    }

    [Fact]
    public async Task Reactivate_WhenNotCanceled_Rejected()
    {
        _billing.OnGetSubscription = id => Fake.Subscription(id, "active", "eshop-pro");

        await Assert.ThrowsAsync<BillingProviderException>(() => CreateService().ReactivateAsync(100));
        Assert.DoesNotContain("Reactivate", _billing.Calls);
    }

    [Fact]
    public async Task Cancel_WhenAlreadyCanceled_Rejected()
    {
        _billing.OnGetSubscription = id => Fake.Subscription(id, "canceled", "eshop-pro");

        await Assert.ThrowsAsync<BillingProviderException>(() => CreateService().CancelAsync(100, true, null));
        Assert.DoesNotContain("Cancel", _billing.Calls);
    }

    [Fact]
    public async Task Pause_WhenActive_TransitionsAndPublishesStateChanged()
    {
        _billing.OnGetSubscription = id => Fake.Subscription(id, "active", "eshop-pro");
        _billing.OnPause = id => Fake.Subscription(id, "on_hold", "eshop-pro");

        var result = await CreateService().PauseAsync(100);

        Assert.Equal("on_hold", result.State);
        var evt = Assert.IsType<SubscriptionStateChanged>(Assert.Single(_publisher.Published));
        Assert.Equal("active", evt.OldState);
        Assert.Equal("on_hold", evt.NewState);
    }
}
