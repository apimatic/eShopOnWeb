using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// UC1 (subscribe) and UC2 (pay-as-you-go usage) rules, exercised against a substituted seam.
/// </summary>
public class SubscriptionServiceSubscribeAndUsage
{
    private readonly SubscriptionServiceHarness _harness = new();

    private const string User = SubscriptionServiceHarness.UserName;

    [Fact]
    public async Task SubscribeCreatesTheCustomerThenTheSubscriptionAndAnnouncesIt()
    {
        _harness.BillingClient.FindPlanByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Plan());
        _harness.BillingClient.EnsureCustomerAsync(Arg.Any<BillingCustomerDetails>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Customer());
        _harness.BillingClient.ListSubscriptionsForCustomerAsync(55001, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());
        _harness.BillingClient.CreateSubscriptionAsync(55001, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub());

        var subscription = await _harness.Service.SubscribeAsync(User, "eshop-pro");

        Assert.Equal(88001, subscription.Id);
        await _harness.BillingClient.Received(1).CreateSubscriptionAsync(55001, "eshop-pro", Arg.Any<CancellationToken>());

        var activated = Assert.IsType<SubscriptionActivated>(Assert.Single(_harness.PublishedNotifications));
        Assert.Equal(User, activated.UserName);
        Assert.Equal(88001, activated.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeUsesTheSignedInIdentityAsTheProviderSideCustomerReference()
    {
        _harness.BillingClient.FindPlanByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Plan());
        _harness.BillingClient.EnsureCustomerAsync(Arg.Any<BillingCustomerDetails>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Customer());
        _harness.BillingClient.ListSubscriptionsForCustomerAsync(55001, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());
        _harness.BillingClient.CreateSubscriptionAsync(55001, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub());

        await _harness.Service.SubscribeAsync(User, "eshop-pro");

        // The reference is what makes repeat subscribes idempotent, so it must be the user's identity.
        await _harness.BillingClient.Received(1).EnsureCustomerAsync(
            Arg.Is<BillingCustomerDetails>(d => d.Reference == User && d.Email == User),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeReturnsTheExistingActiveSubscriptionInsteadOfEnrollingTwice()
    {
        _harness.BillingClient.FindPlanByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Plan());
        _harness.BillingClient.EnsureCustomerAsync(Arg.Any<BillingCustomerDetails>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Customer());
        _harness.BillingClient.ListSubscriptionsForCustomerAsync(55001, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionServiceHarness.Sub(id: 77001) });

        var subscription = await _harness.Service.SubscribeAsync(User, "eshop-pro");

        Assert.Equal(77001, subscription.Id);
        await _harness.BillingClient.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Empty(_harness.PublishedNotifications);
    }

    [Fact]
    public async Task SubscribeIgnoresCancelledSubscriptionsWhenLookingForAnExistingEnrollment()
    {
        _harness.BillingClient.FindPlanByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Plan());
        _harness.BillingClient.EnsureCustomerAsync(Arg.Any<BillingCustomerDetails>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Customer());
        _harness.BillingClient.ListSubscriptionsForCustomerAsync(55001, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionServiceHarness.Sub(id: 77001, state: SubscriptionState.Canceled) });
        _harness.BillingClient.CreateSubscriptionAsync(55001, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub());

        var subscription = await _harness.Service.SubscribeAsync(User, "eshop-pro");

        Assert.Equal(88001, subscription.Id);
    }

    [Fact]
    public async Task SubscribeRefusesAnUnresolvableHandleWithoutTouchingTheCustomerRecord()
    {
        _harness.BillingClient.FindPlanByHandleAsync("gone-away", Arg.Any<CancellationToken>())
            .Returns((SubscriptionPlan?)null);

        var ex = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _harness.Service.SubscribeAsync(User, "gone-away"));

        Assert.Contains("gone-away", ex.Message, StringComparison.Ordinal);
        await _harness.BillingClient.DidNotReceive().EnsureCustomerAsync(
            Arg.Any<BillingCustomerDetails>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeRefusesAnArchivedPlan()
    {
        _harness.BillingClient.FindPlanByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Plan(archived: true));

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _harness.Service.SubscribeAsync(User, "eshop-pro"));
    }

    [Fact]
    public async Task SubscribeStandsEvenWhenTheInProcessNotificationHandlerFails()
    {
        _harness.BillingClient.FindPlanByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Plan());
        _harness.BillingClient.EnsureCustomerAsync(Arg.Any<BillingCustomerDetails>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Customer());
        _harness.BillingClient.ListSubscriptionsForCustomerAsync(55001, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());
        _harness.BillingClient.CreateSubscriptionAsync(55001, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub());
        _harness.Publisher
            .Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("handler blew up"));

        // Eventing is best-effort: the enrollment has already happened and must not be rolled back.
        var subscription = await _harness.Service.SubscribeAsync(User, "eshop-pro");

        Assert.Equal(88001, subscription.Id);
    }

    [Fact]
    public async Task ListSubscriptionsReturnsEmptyForAUserWhoHasNeverSubscribed()
    {
        _harness.BillingClient.FindCustomerByReferenceAsync(User, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        Assert.Empty(await _harness.Service.ListSubscriptionsAsync(User));

        await _harness.BillingClient.DidNotReceive().ListSubscriptionsForCustomerAsync(
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task RecordUsageRejectsANonPositiveQuantityBeforeAnyProviderCall(decimal quantity)
    {
        var ex = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _harness.Service.RecordUsageAsync(88001, quantity, null));

        Assert.Contains("greater than zero", ex.Message, StringComparison.Ordinal);
        await _harness.BillingClient.DidNotReceive().RecordUsageAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordUsageRefusesWhenTheConfiguredComponentIsNotMetered()
    {
        _harness.BillingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Component(isMetered: false));

        var ex = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _harness.Service.RecordUsageAsync(88001, 1m, null));

        Assert.Contains("not metered", ex.Message, StringComparison.Ordinal);
        await _harness.BillingClient.DidNotReceive().RecordUsageAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordUsageRefusesWhenTheComponentHandleDoesNotResolve()
    {
        _harness.BillingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns((MeteredComponent?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _harness.Service.RecordUsageAsync(88001, 1m, null));
    }

    [Fact]
    public async Task RecordUsageRefusesWhenTheComponentLivesOnADifferentProductFamily()
    {
        _harness.BillingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Component(familyHandle: "some-other-family"));

        var ex = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _harness.Service.RecordUsageAsync(88001, 1m, null));

        Assert.Contains("some-other-family", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordUsageRefusesWhenTheComponentIsArchived()
    {
        _harness.BillingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Component(archived: true));

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _harness.Service.RecordUsageAsync(88001, 1m, null));
    }

    [Fact]
    public async Task RecordUsageRefusesAgainstASubscriptionThatIsNotBilling()
    {
        _harness.BillingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Component());
        _harness.BillingClient.FindSubscriptionAsync(88001, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub(state: SubscriptionState.Canceled));

        var ex = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _harness.Service.RecordUsageAsync(88001, 1m, null));

        Assert.Contains("Canceled", ex.Message, StringComparison.Ordinal);
        await _harness.BillingClient.DidNotReceive().RecordUsageAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordUsageRefusesAgainstAnUnknownSubscription()
    {
        _harness.BillingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Component());
        _harness.BillingClient.FindSubscriptionAsync(999999, Arg.Any<CancellationToken>())
            .Returns((Subscription?)null);

        await Assert.ThrowsAsync<BillingProviderNotFoundException>(
            () => _harness.Service.RecordUsageAsync(999999, 1m, null));
    }

    [Fact]
    public async Task RecordUsageReturnsTheRunningPeriodToDateTotal()
    {
        StubHealthyUsagePath();
        _harness.BillingClient.GetPeriodToDateUnitsAsync(88001, 3062733, Arg.Any<CancellationToken>())
            .Returns(17);

        var receipt = await _harness.Service.RecordUsageAsync(88001, 5m, "order 42");

        Assert.Equal(17, receipt.PeriodToDateUnits);
        Assert.True(receipt.PeriodToDateAvailable);
        Assert.Equal(991001L, receipt.Recorded.Id);
    }

    [Fact]
    public async Task RecordUsageStillSucceedsWhenTheRunningTotalCannotBeRead()
    {
        StubHealthyUsagePath();
        _harness.BillingClient.GetPeriodToDateUnitsAsync(88001, 3062733, Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderUnavailableException("GetPeriodToDateUnitsAsync", "gateway timeout"));

        // The usage stands; only the total is unavailable.
        var receipt = await _harness.Service.RecordUsageAsync(88001, 5m, "order 42");

        Assert.Null(receipt.PeriodToDateUnits);
        Assert.False(receipt.PeriodToDateAvailable);
        Assert.Equal(991001L, receipt.Recorded.Id);
    }

    [Fact]
    public async Task RecordUsageForUserReturnsNullWhenTheBuyerHasNoActiveSubscription()
    {
        _harness.BillingClient.FindCustomerByReferenceAsync(User, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Customer());
        _harness.BillingClient.ListSubscriptionsForCustomerAsync(55001, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionServiceHarness.Sub(state: SubscriptionState.Canceled) });

        Assert.Null(await _harness.Service.RecordUsageForUserAsync(User, 1m, "order 42"));

        await _harness.BillingClient.DidNotReceive().RecordUsageAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordUsageForUserMetersOneUnitAgainstTheBuyersActiveSubscription()
    {
        _harness.BillingClient.FindCustomerByReferenceAsync(User, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Customer());
        _harness.BillingClient.ListSubscriptionsForCustomerAsync(55001, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionServiceHarness.Sub() });
        StubHealthyUsagePath();

        var receipt = await _harness.Service.RecordUsageForUserAsync(User, 1m, "eShopOnWeb order 42");

        Assert.NotNull(receipt);
        await _harness.BillingClient.Received(1).RecordUsageAsync(
            88001, 3062733, 1m, "eShopOnWeb order 42", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPeriodToDateUnitsVerifiesTheComponentBeforeReadingTheBalance()
    {
        _harness.BillingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Component());
        _harness.BillingClient.GetPeriodToDateUnitsAsync(88001, 3062733, Arg.Any<CancellationToken>())
            .Returns(23);

        Assert.Equal(23, await _harness.Service.GetPeriodToDateUnitsAsync(88001));
    }

    [Fact]
    public async Task GetPeriodToDateUnitsRefusesWhenTheComponentIsMisconfigured()
    {
        _harness.BillingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Component(isMetered: false));

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _harness.Service.GetPeriodToDateUnitsAsync(88001));

        await _harness.BillingClient.DidNotReceive().GetPeriodToDateUnitsAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetVerifiedMeteredComponentRefusesWhenNoHandleIsConfigured()
    {
        _harness.CatalogSettings.MeteredComponentHandle.Returns(string.Empty);

        var ex = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _harness.Service.GetVerifiedMeteredComponentAsync());

        Assert.Contains("Maxio:MeteredComponentHandle", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetVerifiedMeteredComponentReturnsTheComponentWhenEverythingLinesUp()
    {
        _harness.BillingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Component());

        var component = await _harness.Service.GetVerifiedMeteredComponentAsync();

        Assert.True(component.IsMetered);
        Assert.Equal(3062733, component.Id);
    }

    private void StubHealthyUsagePath()
    {
        _harness.BillingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Component());
        _harness.BillingClient.FindSubscriptionAsync(88001, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceHarness.Sub());
        _harness.BillingClient.RecordUsageAsync(
                88001, 3062733, Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => SubscriptionServiceHarness.Usage(ci.ArgAt<decimal>(2)));
    }
}
