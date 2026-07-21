using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.MaxioIntegrationTests.Support;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// End-to-end, real-provider coverage of UC1 (subscribe) through UC4 (lifecycle), run against the
/// live Maxio sandbox with a fresh, uniquely-referenced customer per test run so repeated runs never
/// collide with each other or with manual testing. One cohesive scenario is used (rather than many
/// independent tests) so the sandbox only accumulates one throwaway customer/subscription per run.
/// </summary>
public class SubscriptionLifecycleTests
{
    [Fact]
    public async Task FullSubscriptionLifecycle_SubscribeUsagePlanChangePauseResumeCancelReactivate_WorksEndToEnd()
    {
        var client = MaxioBillingClientTestFactory.CreateLive(out _);
        var reference = $"itest-{Guid.NewGuid():N}";
        var email = $"{reference}@example.com";

        // --- UC1: ensure-customer is idempotent on the reference ---
        var profile = new BillingCustomerProfile(reference, email, "Integration", "Test");
        var customer = await client.EnsureCustomerAsync(profile);
        Assert.True(customer.Id > 0);
        Assert.Equal(reference, customer.Reference);

        var customerAgain = await client.EnsureCustomerAsync(profile);
        Assert.Equal(customer.Id, customerAgain.Id); // idempotent - no duplicate created

        // A brand-new customer has no subscriptions yet.
        var subscriptionsBeforeEnrollment = await client.ListCustomerSubscriptionsAsync(customer.Id);
        Assert.Empty(subscriptionsBeforeEnrollment);

        // --- UC1: enroll in eshop-pro ---
        var subscription = await client.CreateSubscriptionAsync(customer.Id, "eshop-pro");
        Assert.True(subscription.Id > 0);
        Assert.Equal(BillingSubscriptionState.Active, subscription.State);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Equal(29900L, subscription.PriceInCents);
        Assert.Equal(customer.Id, subscription.CustomerId);

        var listedAfterEnrollment = await client.ListCustomerSubscriptionsAsync(customer.Id);
        Assert.Single(listedAfterEnrollment, s => s.Id == subscription.Id);

        var reRead = await client.GetSubscriptionAsync(subscription.Id);
        Assert.Equal(subscription.Id, reRead.Id);
        Assert.Equal(BillingSubscriptionState.Active, reRead.State);

        // --- UC2: metered usage accumulates correctly ---
        var firstUsage = await client.RecordUsageAsync(subscription.Id, quantity: 3, memo: "integration test - first batch");
        Assert.Equal(3, firstUsage.Quantity);
        if (firstUsage.PeriodToDateBalance.HasValue)
        {
            Assert.Equal(3, firstUsage.PeriodToDateBalance.Value);
        }

        var secondUsage = await client.RecordUsageAsync(subscription.Id, quantity: 2, memo: "integration test - second batch");
        Assert.Equal(2, secondUsage.Quantity);

        var balance = await client.GetUsageBalanceAsync(subscription.Id);
        Assert.Equal(5, balance.UnitBalance); // 3 + 2, accumulated - not just "some positive number"

        // --- UC3: preview a plan change (read-only - must not alter the subscription) ---
        var preview = await client.PreviewPlanChangeAsync(subscription.Id, "basic-plan");
        Assert.Equal("basic-plan", preview.TargetPlanHandle);
        // ChargeInCents is the new plan's raw cost (always positive) - the "would this cost the
        // customer anything right now" signal for a downgrade is PaymentDueInCents, which must never
        // be positive when moving to a cheaper plan mid-period.
        Assert.True(preview.PaymentDueInCents <= 0);
        Assert.True(preview.CreditAppliedInCents < 0); // a credit was generated for the unused time on the pricier plan

        var unchangedAfterPreview = await client.GetSubscriptionAsync(subscription.Id);
        Assert.Equal("eshop-pro", unchangedAfterPreview.ProductHandle); // preview alone changes nothing

        // --- UC3: schedule a delayed (no-proration) plan change ---
        var scheduled = await client.SchedulePlanChangeAsync(subscription.Id, "basic-plan");
        Assert.Equal("eshop-pro", scheduled.ProductHandle); // current plan unchanged until renewal
        Assert.Equal("basic-plan", scheduled.NextProductHandle); // scheduled change recorded

        // --- UC4: pause / resume ---
        var paused = await client.PauseSubscriptionAsync(subscription.Id);
        Assert.Equal(BillingSubscriptionState.Paused, paused.State);

        var resumed = await client.ResumeSubscriptionAsync(subscription.Id);
        Assert.Equal(BillingSubscriptionState.Active, resumed.State);

        // --- UC4: cancel (immediate) then reactivate ---
        var cancelled = await client.CancelSubscriptionAsync(subscription.Id, endOfPeriod: false, reason: "integration test cleanup");
        Assert.Equal(BillingSubscriptionState.Canceled, cancelled.State);

        var reactivated = await client.ReactivateSubscriptionAsync(subscription.Id);
        Assert.True(reactivated.State is BillingSubscriptionState.Active or BillingSubscriptionState.Trialing);
    }
}
