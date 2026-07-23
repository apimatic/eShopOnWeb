using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// The domain rules layered over the provider seam (UC1–UC4). These are the decisions that must be taken
/// before Maxio is called at all, plus the guarantee that a completed billing operation is never undone by
/// a failing in-process notification.
/// </summary>
public class SubscriptionServiceTests
{
    private const string User = "demouser@microsoft.com";
    private const string OtherUser = "someone.else@microsoft.com";

    private readonly FakeBillingClient _billing = new();
    private readonly RecordingPublisher _publisher = new();
    private readonly SubscriptionService _service;

    public SubscriptionServiceTests()
    {
        _billing.Plans.Add(Plan("eshop-pro", 299.00m));
        _billing.Plans.Add(Plan("basic-plan", 29.00m));
        _billing.Component = new MeteredComponent(
            MaxioPayloads.ComponentId, "api-call", "API Calls", "metered_component", isMetered: true,
            unitPrice: 0.01m, pricingScheme: "per_unit");

        _service = new SubscriptionService(_billing, new TestSettings(), _publisher,
            new NullAppLogger<SubscriptionService>());
    }

    // -------------------------------------------------------------------------------------------------
    // UC1 — subscribe
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task SubscribeAsync_CreatesTheCustomerThenEnrolls()
    {
        _billing.CreatedSubscription = Subscription(SubscriptionState.Active);

        var subscription = await _service.SubscribeAsync(User, "eshop-pro");

        Assert.Equal(MaxioPayloads.SubscriptionId, subscription.Id);
        Assert.Contains($"CreateCustomerAsync:{User}", _billing.Calls);
        Assert.Contains("CreateSubscriptionAsync:eshop-pro", _billing.Calls);

        // The eShopOnWeb user name is what makes a repeat subscribe idempotent provider-side.
        Assert.Equal(User, _billing.LastRegistration!.Reference);
        Assert.Equal(User, _billing.LastRegistration.Email);
    }

    [Fact]
    public async Task SubscribeAsync_ReusesAnExistingCustomer()
    {
        _billing.ExistingCustomer = new BillingCustomer(MaxioPayloads.CustomerId, User, User, "Demo", "User");
        _billing.CreatedSubscription = Subscription(SubscriptionState.Active);

        await _service.SubscribeAsync(User, "eshop-pro");

        Assert.True(_billing.NeverCalled("CreateCustomerAsync"));
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsTheExistingLiveSubscription_InsteadOfEnrollingTwice()
    {
        _billing.ExistingCustomer = new BillingCustomer(MaxioPayloads.CustomerId, User, User, "Demo", "User");
        _billing.CustomerSubscriptions.Add(Subscription(SubscriptionState.Active));

        var subscription = await _service.SubscribeAsync(User, "eshop-pro");

        Assert.Equal(MaxioPayloads.SubscriptionId, subscription.Id);

        // The double-click case: no second enrollment, and no second activation announcement.
        Assert.True(_billing.NeverCalled("CreateSubscriptionAsync"));
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task SubscribeAsync_EnrollsWhenTheOnlyExistingSubscriptionIsCancelled()
    {
        _billing.ExistingCustomer = new BillingCustomer(MaxioPayloads.CustomerId, User, User, "Demo", "User");
        _billing.CustomerSubscriptions.Add(Subscription(SubscriptionState.Cancelled));
        _billing.CreatedSubscription = Subscription(SubscriptionState.Active);

        await _service.SubscribeAsync(User, "eshop-pro");

        Assert.Contains("CreateSubscriptionAsync:eshop-pro", _billing.Calls);
    }

    [Fact]
    public async Task SubscribeAsync_PublishesSubscriptionActivated()
    {
        _billing.CreatedSubscription = Subscription(SubscriptionState.Active);

        await _service.SubscribeAsync(User, "eshop-pro");

        var activated = Assert.IsType<SubscriptionActivated>(Assert.Single(_publisher.Published));
        Assert.Equal(User, activated.UserName);
        Assert.Equal(MaxioPayloads.SubscriptionId, activated.SubscriptionId);
        Assert.Equal("eshop-pro", activated.PlanHandle);
        Assert.Equal(299.00m, activated.PlanPrice);
    }

    [Fact]
    public async Task SubscribeAsync_Succeeds_EvenWhenTheNotificationHandlerThrows()
    {
        _billing.CreatedSubscription = Subscription(SubscriptionState.Active);
        _publisher.Failure = new InvalidOperationException("the e-mail handler blew up");

        var subscription = await _service.SubscribeAsync(User, "eshop-pro");

        // Best-effort eventing: the enrollment stands (plan.md §2.5).
        Assert.Equal(MaxioPayloads.SubscriptionId, subscription.Id);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsAnUnknownPlan_BeforeTouchingTheCustomer()
    {
        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _service.SubscribeAsync(User, "ghost-plan"));

        Assert.Contains("ghost-plan", exception.Message);
        Assert.True(_billing.NeverCalled("CreateCustomerAsync"));
        Assert.True(_billing.NeverCalled("CreateSubscriptionAsync"));
    }

    [Fact]
    public async Task SubscribeAsync_RejectsAnArchivedPlan()
    {
        _billing.Plans.Add(new BillingPlan(1, "retired", "Retired", null, 10m, 1, "month", false, archived: true));

        await Assert.ThrowsAsync<BillingConfigurationException>(() => _service.SubscribeAsync(User, "retired"));
        Assert.True(_billing.NeverCalled("CreateSubscriptionAsync"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SubscribeAsync_RejectsAMissingUserReference(string? userReference)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _service.SubscribeAsync(userReference!, "eshop-pro"));
        Assert.Empty(_billing.Calls);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEmpty_ForAUserWithNoProviderCustomer()
    {
        Assert.Empty(await _service.ListSubscriptionsAsync("nobody@microsoft.com"));
        Assert.True(_billing.NeverCalled("ListSubscriptionsForCustomerAsync"));
    }

    [Fact]
    public async Task FindActiveSubscriptionAsync_IgnoresCancelledSubscriptions()
    {
        _billing.ExistingCustomer = new BillingCustomer(MaxioPayloads.CustomerId, User, User, "Demo", "User");
        _billing.CustomerSubscriptions.Add(Subscription(SubscriptionState.Cancelled));

        Assert.Null(await _service.FindActiveSubscriptionAsync(User));
    }

    // -------------------------------------------------------------------------------------------------
    // UC2 — usage
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RecordUsageAsync_ReturnsTheRunningTotalAndItsCharge()
    {
        _billing.SubscriptionById = Subscription(SubscriptionState.Active);
        _billing.RecordedUsage = new UsageRecord(8801, 5, "order #42", DateTimeOffset.UnixEpoch);
        _billing.PeriodToDate = 250;

        var summary = await _service.RecordUsageAsync(MaxioPayloads.SubscriptionId, 5, "order #42", User);

        Assert.Equal(5, summary.Record!.Quantity);
        Assert.Equal(250, summary.PeriodToDateQuantity);
        Assert.Equal(0.01m, summary.UnitPrice);
        // 250 units at $0.01 is $2.50 — not $250 and not $0.025.
        Assert.Equal(2.50m, summary.PeriodToDateCharge);
        Assert.False(summary.TotalUnavailable);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task RecordUsageAsync_RejectsANonPositiveQuantity_BeforeAnyProviderCall(int quantity)
    {
        _billing.SubscriptionById = Subscription(SubscriptionState.Active);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.RecordUsageAsync(MaxioPayloads.SubscriptionId, quantity, null, User));

        Assert.Contains("greater than zero", exception.Message);
        Assert.Empty(_billing.Calls);
    }

    [Theory]
    [InlineData(SubscriptionState.Cancelled)]
    [InlineData(SubscriptionState.Expired)]
    [InlineData(SubscriptionState.Paused)]
    [InlineData(SubscriptionState.Unknown)]
    public async Task RecordUsageAsync_RefusesASubscriptionThatIsNotLive(SubscriptionState state)
    {
        _billing.SubscriptionById = Subscription(state);

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.RecordUsageAsync(MaxioPayloads.SubscriptionId, 1, null, User));

        Assert.True(_billing.NeverCalled("RecordUsageAsync"));
    }

    [Fact]
    public async Task RecordUsageAsync_RefusesANonMeteredComponent()
    {
        _billing.SubscriptionById = Subscription(SubscriptionState.Active);
        _billing.Component = new MeteredComponent(1, "api-call", "API Calls", "quantity_based_component",
            isMetered: false, unitPrice: 0.01m, pricingScheme: "per_unit");

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _service.RecordUsageAsync(MaxioPayloads.SubscriptionId, 1, null, User));

        Assert.Contains("not metered", exception.Message);
        Assert.True(_billing.NeverCalled("RecordUsageAsync"));
    }

    [Fact]
    public async Task RecordUsageAsync_RefusesAMissingComponent()
    {
        _billing.SubscriptionById = Subscription(SubscriptionState.Active);
        _billing.Component = null;

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _service.RecordUsageAsync(MaxioPayloads.SubscriptionId, 1, null, User));

        Assert.True(_billing.NeverCalled("RecordUsageAsync"));
    }

    [Fact]
    public async Task RecordUsageAsync_KeepsTheUsage_WhenTheTotalCannotBeReadBack()
    {
        _billing.SubscriptionById = Subscription(SubscriptionState.Active);
        _billing.RecordedUsage = new UsageRecord(8802, 3, null, DateTimeOffset.UnixEpoch);
        _billing.PeriodToDateFailure = new BillingProviderException("read-back timed out");

        var summary = await _service.RecordUsageAsync(MaxioPayloads.SubscriptionId, 3, null, User);

        // The usage stands; only the running total is reported unavailable (plan.md UC2).
        Assert.Equal(3, summary.Record!.Quantity);
        Assert.True(summary.TotalUnavailable);
        Assert.Null(summary.PeriodToDateCharge);
    }

    [Fact]
    public async Task RecordUsageAsync_RefusesASubscriptionBelongingToSomebodyElse()
    {
        _billing.SubscriptionById = Subscription(SubscriptionState.Active);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.RecordUsageAsync(MaxioPayloads.SubscriptionId, 1, null, OtherUser));

        // Reported as "not found" so a customer cannot probe for other people's subscription ids.
        Assert.Contains("No subscription with id", exception.Message);
        Assert.True(_billing.NeverCalled("RecordUsageAsync"));
    }

    [Fact]
    public async Task RecordUsageAsync_AllowsAnAdministratorToActOnAnySubscription()
    {
        _billing.SubscriptionById = Subscription(SubscriptionState.Active);
        _billing.PeriodToDate = 1;

        var summary = await _service.RecordUsageAsync(
            MaxioPayloads.SubscriptionId, 1, null, restrictToUserReference: null);

        Assert.NotNull(summary.Record);
    }

    [Fact]
    public async Task RecordUsageForUserAsync_ReturnsNull_WhenTheUserHasNoLiveSubscription()
    {
        _billing.ExistingCustomer = new BillingCustomer(MaxioPayloads.CustomerId, User, User, "Demo", "User");

        // The order-placed hook must not fail checkout for a shopper with no subscription.
        Assert.Null(await _service.RecordUsageForUserAsync(User, 1, "order #7"));
        Assert.True(_billing.NeverCalled("RecordUsageAsync"));
    }

    [Fact]
    public async Task RecordUsageForUserAsync_MetersTheUsersLiveSubscription()
    {
        _billing.ExistingCustomer = new BillingCustomer(MaxioPayloads.CustomerId, User, User, "Demo", "User");
        _billing.CustomerSubscriptions.Add(Subscription(SubscriptionState.Active));
        _billing.SubscriptionById = Subscription(SubscriptionState.Active);
        _billing.PeriodToDate = 12;

        var summary = await _service.RecordUsageForUserAsync(User, 1, "order #7");

        Assert.NotNull(summary);
        Assert.Equal(12, summary!.PeriodToDateQuantity);
        Assert.Contains($"RecordUsageAsync:{MaxioPayloads.SubscriptionId}:1", _billing.Calls);
    }

    [Fact]
    public async Task GetUsageSummaryAsync_ReportsTheTotalWithoutRecordingAnything()
    {
        _billing.SubscriptionById = Subscription(SubscriptionState.Active);
        _billing.PeriodToDate = 40;

        var summary = await _service.GetUsageSummaryAsync(MaxioPayloads.SubscriptionId, User);

        Assert.NotNull(summary);
        Assert.Null(summary!.Record);
        Assert.Equal(40, summary.PeriodToDateQuantity);
        Assert.Equal(0.40m, summary.PeriodToDateCharge);
        Assert.True(_billing.NeverCalled("RecordUsageAsync"));
    }

    // -------------------------------------------------------------------------------------------------
    // UC3 — plan change
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ChangePlanAsync_CommitsWhenThePreviewStillHolds()
    {
        _billing.SubscriptionById = Subscription(SubscriptionState.Active);
        _billing.Preview = PreviewOf(135.50m);
        _billing.UpdatedSubscription = Subscription(SubscriptionState.Active, "basic-plan", 29.00m);

        var updated = await _service.ChangePlanAsync(
            MaxioPayloads.SubscriptionId, "basic-plan", PlanChangeTiming.Immediately, 135.50m, User);

        Assert.Equal("basic-plan", updated.PlanHandle);
        Assert.Contains("ChangePlanAsync:basic-plan:Immediately", _billing.Calls);
    }

    [Fact]
    public async Task ChangePlanAsync_RejectsAStalePreview_AndCommitsNothing()
    {
        _billing.SubscriptionById = Subscription(SubscriptionState.Active);
        _billing.Preview = PreviewOf(150.00m);
        _billing.UpdatedSubscription = Subscription(SubscriptionState.Active, "basic-plan", 29.00m);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ChangePlanAsync(
                MaxioPayloads.SubscriptionId, "basic-plan", PlanChangeTiming.Immediately, 135.50m, User));

        // The customer must never be charged an amount they were not shown (plan.md UC3).
        Assert.Contains("$135.50", exception.Message);
        Assert.Contains("$150.00", exception.Message);
        Assert.True(_billing.NeverCalled("ChangePlanAsync"));
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task ChangePlanAsync_RejectsAChangeToThePlanAlreadyInEffect()
    {
        _billing.SubscriptionById = Subscription(SubscriptionState.Active);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ChangePlanAsync(
                MaxioPayloads.SubscriptionId, "eshop-pro", PlanChangeTiming.Immediately, 0m, User));

        Assert.Contains("already on plan", exception.Message);
        Assert.True(_billing.NeverCalled("PreviewPlanChangeAsync"));
        Assert.True(_billing.NeverCalled("ChangePlanAsync"));
    }

    [Fact]
    public async Task ChangePlanAsync_RejectsAChangeOnACancelledSubscription()
    {
        _billing.SubscriptionById = Subscription(SubscriptionState.Cancelled);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ChangePlanAsync(
                MaxioPayloads.SubscriptionId, "basic-plan", PlanChangeTiming.Immediately, 0m, User));

        Assert.Contains("Reactivate it first", exception.Message);
        Assert.True(_billing.NeverCalled("ChangePlanAsync"));
    }

    [Fact]
    public async Task ChangePlanAsync_PublishesSubscriptionPlanChanged()
    {
        _billing.SubscriptionById = Subscription(SubscriptionState.Active);
        _billing.Preview = PreviewOf(-135.50m);
        _billing.UpdatedSubscription = Subscription(SubscriptionState.Active, "basic-plan", 29.00m);

        await _service.ChangePlanAsync(
            MaxioPayloads.SubscriptionId, "basic-plan", PlanChangeTiming.Immediately, -135.50m, User);

        var changed = Assert.IsType<SubscriptionPlanChanged>(Assert.Single(_publisher.Published));
        Assert.Equal("eshop-pro", changed.PreviousPlanHandle);
        Assert.Equal("basic-plan", changed.NewPlanHandle);
        Assert.Equal(-135.50m, changed.ProrationAmount);
        Assert.True(changed.AppliedImmediately);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_RefusesASubscriptionBelongingToSomebodyElse()
    {
        _billing.SubscriptionById = Subscription(SubscriptionState.Active);

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.PreviewPlanChangeAsync(
                MaxioPayloads.SubscriptionId, "basic-plan", PlanChangeTiming.Immediately, OtherUser));

        Assert.True(_billing.NeverCalled("PreviewPlanChangeAsync"));
    }

    // -------------------------------------------------------------------------------------------------
    // UC4 — lifecycle
    // -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Reactivate)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Cancelled, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Cancelled, SubscriptionLifecycleAction.CancelImmediately)]
    [InlineData(SubscriptionState.Unknown, SubscriptionLifecycleAction.Resume)]
    public async Task ApplyLifecycleActionAsync_RejectsAnIllegalTransition_WithoutCallingTheProvider(
        SubscriptionState state, SubscriptionLifecycleAction action)
    {
        _billing.SubscriptionById = Subscription(state);
        _billing.UpdatedSubscription = Subscription(SubscriptionState.Active);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ApplyLifecycleActionAsync(MaxioPayloads.SubscriptionId, action, null, User));

        Assert.Contains("not allowed", exception.Message);
        Assert.True(_billing.NeverCalled("ApplyLifecycleActionAsync"));
        Assert.Empty(_publisher.Published);
    }

    [Theory]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Pause)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.CancelAtEndOfPeriod)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.CancelImmediately)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Resume)]
    [InlineData(SubscriptionState.Cancelled, SubscriptionLifecycleAction.Reactivate)]
    public async Task ApplyLifecycleActionAsync_AllowsALegalTransition(
        SubscriptionState state, SubscriptionLifecycleAction action)
    {
        _billing.SubscriptionById = Subscription(state);
        _billing.UpdatedSubscription = Subscription(SubscriptionState.Active);

        await _service.ApplyLifecycleActionAsync(MaxioPayloads.SubscriptionId, action, null, User);

        Assert.Contains($"ApplyLifecycleActionAsync:{action}", _billing.Calls);
    }

    [Fact]
    public async Task ApplyLifecycleActionAsync_PublishesTheOldAndNewState()
    {
        _billing.SubscriptionById = Subscription(SubscriptionState.Active);
        _billing.UpdatedSubscription = Subscription(SubscriptionState.Paused);

        await _service.ApplyLifecycleActionAsync(
            MaxioPayloads.SubscriptionId, SubscriptionLifecycleAction.Pause, "on holiday", User);

        var changed = Assert.IsType<SubscriptionStateChanged>(Assert.Single(_publisher.Published));
        Assert.Equal(SubscriptionState.Active, changed.PreviousState);
        Assert.Equal(SubscriptionState.Paused, changed.NewState);
        Assert.Equal(SubscriptionLifecycleAction.Pause, changed.Action);
        Assert.Equal("on holiday", changed.Reason);
    }

    [Fact]
    public async Task ApplyLifecycleActionAsync_Succeeds_EvenWhenTheNotificationHandlerThrows()
    {
        _billing.SubscriptionById = Subscription(SubscriptionState.Active);
        _billing.UpdatedSubscription = Subscription(SubscriptionState.Paused);
        _publisher.Failure = new InvalidOperationException("the audit handler blew up");

        var updated = await _service.ApplyLifecycleActionAsync(
            MaxioPayloads.SubscriptionId, SubscriptionLifecycleAction.Pause, null, User);

        Assert.Equal(SubscriptionState.Paused, updated.State);
    }

    [Fact]
    public async Task ApplyLifecycleActionAsync_RefusesAnUnknownSubscriptionId()
    {
        _billing.SubscriptionById = null;

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.ApplyLifecycleActionAsync(4242, SubscriptionLifecycleAction.Pause, null, User));

        Assert.Contains("4242", exception.Message);
    }

    // -------------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------------

    private static BillingPlan Plan(string handle, decimal price) =>
        new(handle.GetHashCode(), handle, handle, null, price, 1, "month", false, false);

    private static Subscription Subscription(SubscriptionState state, string planHandle = "eshop-pro",
        decimal price = 299.00m) => new(
        MaxioPayloads.SubscriptionId, User, MaxioPayloads.CustomerId, planHandle, "Pro Plan", price, state,
        state.ToString().ToLowerInvariant(),
        new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        null);

    private static PlanChangePreview PreviewOf(decimal net) => new(
        MaxioPayloads.SubscriptionId, "eshop-pro", "basic-plan", PlanChangeTiming.Immediately,
        prorationCharge: net > 0 ? net : 0m,
        prorationCredit: net > 0 ? 0m : -net,
        newPlanPrice: 29.00m,
        effectiveAt: DateTimeOffset.UnixEpoch);

    private sealed class TestSettings : ISubscriptionSettings
    {
        public string DefaultProductHandle => "eshop-pro";
        public string AlternateProductHandle => "basic-plan";
        public string MeteredComponentHandle => "api-call";
    }

    /// <summary>Records what was published, and can be made to fail the way a bad handler would.</summary>
    private sealed class RecordingPublisher : IPublisher
    {
        public List<INotification> Published { get; } = new();

        public Exception? Failure { get; set; }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is INotification typed)
            {
                return Publish(typed, cancellationToken);
            }

            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Published.Add(notification);
            return Task.CompletedTask;
        }
    }
}
