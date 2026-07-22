using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

/// <summary>UC2 — pay-as-you-go usage.</summary>
public class RecordUsageAsync
{
    private const string UserReference = "demouser@microsoft.com";
    private const int CustomerId = 90210;

    private static readonly BillingPlan ProPlan = new(1, "eshop-pro", "Pro Plan", 299.00m, 1, "month");

    private static (SubscriptionService Service, FakeBillingClient Billing) Build(
        SubscriptionState state = SubscriptionState.Active,
        string userReference = UserReference)
    {
        var billing = new FakeBillingClient();
        billing.Plans.Add(ProPlan);
        billing.Customer = new BillingCustomer(CustomerId, userReference, userReference);
        billing.Subscriptions.Add(new Subscription(50, userReference, CustomerId, ProPlan, state,
            state.ToString().ToLowerInvariant()));

        var service = new SubscriptionService(billing, new RecordingPublisher(),
            new RecordingLogger<SubscriptionService>());

        return (service, billing);
    }

    [Fact]
    public async Task RecordsTheUsageAndReportsTheRunningPeriodTotal()
    {
        var (service, billing) = Build();
        billing.PeriodToDateQuantity = 12;

        var report = await service.RecordUsageAsync(50, 3, "order 7", UserReference);

        Assert.Equal(3, report.Recorded.Quantity);
        Assert.True(report.IsTotalAvailable);
        Assert.Equal(12, report.PeriodToDateQuantity);
    }

    [Fact]
    public async Task PricesThePeriodToDateChargeFromTheComponentsUnitPrice()
    {
        var (service, billing) = Build();
        billing.PeriodToDateQuantity = 250;

        var report = await service.RecordUsageAsync(50, 1, null, UserReference);

        // 250 units at $0.01 is $2.50 — not $250, and not $0.025.
        Assert.Equal(2.50m, report.PeriodToDateCharge);
    }

    [Fact]
    public async Task ReportsSuccessWithTheTotalMarkedUnavailableWhenTheReadBackFails()
    {
        var (service, billing) = Build();
        billing.PeriodToDateFailure = new BillingProviderException("Maxio could not be reached.");

        var report = await service.RecordUsageAsync(50, 4, null, UserReference);

        // The usage was accepted. Turning a failed read of the running total into a failed write
        // would invite a resend, and usage is additive — that would double-bill.
        Assert.Equal(4, report.Recorded.Quantity);
        Assert.False(report.IsTotalAvailable);
        Assert.Null(report.PeriodToDateQuantity);
        Assert.False(string.IsNullOrWhiteSpace(report.TotalUnavailableReason));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task RejectsANonPositiveQuantityBeforeAnyProviderCall(int quantity)
    {
        var (service, billing) = Build();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.RecordUsageAsync(50, quantity, null, UserReference));

        Assert.Empty(billing.Calls);
    }

    [Theory]
    [InlineData(SubscriptionState.Canceled)]
    [InlineData(SubscriptionState.Paused)]
    [InlineData(SubscriptionState.Expired)]
    public async Task RefusesUsageOnASubscriptionThatIsNotBilling(SubscriptionState state)
    {
        var (service, billing) = Build(state);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => service.RecordUsageAsync(50, 1, null, UserReference));

        Assert.Equal(state, exception.CurrentState);
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("RecordUsage:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefusesToRecordUsageWhenTheConfiguredComponentIsNotMetered()
    {
        var (service, billing) = Build();
        billing.ComponentFailure = new BillingConfigurationException(
            "Component 'api-call' is of kind 'quantity_based_component', not metered.");

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => service.RecordUsageAsync(50, 1, null, UserReference));

        // The gate runs before the write, so nothing is billed against the wrong component.
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("RecordUsage:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefusesToRecordUsageOnAnotherCustomersSubscription()
    {
        var (service, billing) = Build();

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => service.RecordUsageAsync(50, 1, null, "someone.else@example.com"));

        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("RecordUsage:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AllowsAnAdministratorToRecordUsageOnAnySubscription()
    {
        var (service, _) = Build();

        // A null acting scope is the administrator surface; it may reach any subscription.
        var report = await service.RecordUsageAsync(50, 2, null, actingUserReference: null);

        Assert.Equal(2, report.Recorded.Quantity);
    }

    [Fact]
    public async Task RecordsOneUnitPerOrderAgainstTheBuyersActiveSubscription()
    {
        var (service, billing) = Build();
        billing.PeriodToDateQuantity = 1;

        var reports = await service.RecordUsageForUserAsync(UserReference, 1, "eShopOnWeb order 42");

        Assert.Single(reports);
        Assert.Equal(1, reports[0].Recorded.Quantity);
        Assert.Contains("RecordUsage:50:1", billing.Calls);
    }

    [Fact]
    public async Task RecordsNothingWhenTheBuyerHasNoSubscriptionAtAll()
    {
        var billing = new FakeBillingClient();
        var service = new SubscriptionService(billing, new RecordingPublisher(),
            new RecordingLogger<SubscriptionService>());

        var reports = await service.RecordUsageForUserAsync("shopper@example.com", 1, "order 1");

        // Most shoppers never subscribe. Checkout must not fail for them.
        Assert.Empty(reports);
    }

    [Fact]
    public async Task RecordsNothingWhenTheBuyersOnlySubscriptionIsNotBilling()
    {
        var (service, billing) = Build(SubscriptionState.Canceled);

        var reports = await service.RecordUsageForUserAsync(UserReference, 1, "order 1");

        Assert.Empty(reports);
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("RecordUsage:", StringComparison.Ordinal));
    }
}
