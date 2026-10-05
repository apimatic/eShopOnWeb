using System.Diagnostics;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class SubscriptionServiceTests
{
    private const string UserName = "demouser@microsoft.com";
    private const string UserId = "user-1";
    private static readonly SubscriptionPlan s_pro = new(1, "eshop-pro", "Pro Plan", null, 29900, 1, "month");

    private readonly IBillingGateway _gateway = Substitute.For<IBillingGateway>();
    private readonly FakeSubscriptionStore _store = new();
    private readonly IShopperDirectory _shoppers = Substitute.For<IShopperDirectory>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

    public SubscriptionServiceTests()
    {
        _shoppers.FindByUserNameAsync(UserName, Arg.Any<CancellationToken>())
            .Returns(new Shopper(UserId, UserName, UserName));
        _gateway.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns(new SubscriptionPlanCatalog(new[] { s_pro }, IsTruncated: false));
        _gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>())
            .Returns(ci => Subscription(ci.Arg<NewBillingSubscription>().Reference));
    }

    private SubscriptionService CreateService(TimeSpan? budget = null) =>
        new(_gateway, _store, _shoppers, _logger, _clock, budget ?? TimeSpan.FromSeconds(25));

    private static BillingSubscription Subscription(string reference, string state = "active") =>
        new(4242, reference, state, "eshop-pro", "Pro Plan", 29900, "USD", 1, "month",
            new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void GivenLinkedCustomer(int customerId)
    {
        var customer = new BillingCustomer(UserId, _clock.GetUtcNow());
        customer.Link(customerId);
        _store.Customers[UserId] = customer;
    }

    [Fact]
    public async Task CreatesCustomerThenSubscriptionAndRecordsBoth()
    {
        _gateway.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingCustomerAccount?)null);
        _gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomerAccount(7, BillingCustomer.ReferenceFor(UserId)));

        var result = await CreateService().SubscribeAsync(UserName, "eshop-pro", "Demo", "User", default);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(7, _store.Customers[UserId].BillingCustomerId);
        var enrollment = _store.Enrollments[SubscriptionEnrollment.KeyFor(UserId, "eshop-pro")];
        Assert.Equal(EnrollmentStatus.Completed, enrollment.Status);
        Assert.Equal(4242, enrollment.BillingSubscriptionId);
        await _gateway.Received(1).CreateCustomerAsync(
            Arg.Is<NewBillingCustomer>(c => c.Reference == "eshop-user-user-1" && c.Email == UserName
                                            && c.FirstName == "Demo" && c.LastName == "User"),
            Arg.Any<CancellationToken>());
        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewBillingSubscription>(s => s.CustomerId == 7 && s.PlanHandle == "eshop-pro"
                                                && s.Reference == enrollment.BillingReference),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReusesLinkedCustomerWithoutCallingMaxioCustomerApis()
    {
        GivenLinkedCustomer(9);

        await CreateService().SubscribeAsync(UserName, "eshop-pro", null, null, default);

        await _gateway.DidNotReceiveWithAnyArgs().FindCustomerByReferenceAsync(default!, default);
        await _gateway.DidNotReceiveWithAnyArgs().CreateCustomerAsync(default!, default);
        await _gateway.Received(1).CreateSubscriptionAsync(Arg.Is<NewBillingSubscription>(s => s.CustomerId == 9),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsPlanNotInCatalogWithoutWritingAnything()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(UserName, "api-call", null, null, default));

        Assert.Empty(_store.Enrollments);
        Assert.Empty(_store.Customers);
        await _gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default);
    }

    [Fact]
    public async Task RefusesSecondRequestWhileFirstIsInFlight()
    {
        GivenLinkedCustomer(7);
        var inFlight = new SubscriptionEnrollment(UserId, "eshop-pro", _clock.GetUtcNow());
        _store.Enrollments[inFlight.Id] = inFlight;

        await Assert.ThrowsAsync<SubscriptionInProgressException>(
            () => CreateService().SubscribeAsync(UserName, "eshop-pro", null, null, default));

        await _gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default);
        Assert.Same(inFlight, _store.Enrollments[inFlight.Id]);
    }

    [Fact]
    public async Task ReturnsExistingSubscriptionWhenPlanAlreadyHeld()
    {
        GivenLinkedCustomer(7);
        var done = new SubscriptionEnrollment(UserId, "eshop-pro", _clock.GetUtcNow());
        done.MarkCompleted(4242, _clock.GetUtcNow());
        _store.Enrollments[done.Id] = done;
        _gateway.FindSubscriptionByReferenceAsync(done.BillingReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(done.BillingReference));

        var result = await CreateService().SubscribeAsync(UserName, "eshop-pro", null, null, default);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(4242, result.Subscription.Id);
        await _gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default);
    }

    [Fact]
    public async Task SettlesUnknownCreateOutcomeByReference()
    {
        GivenLinkedCustomer(7);
        _gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(BillingProviderException.NoResponse("Maxio did not respond while creating the subscription.", outcomeUnknown: true));
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Subscription(ci.Arg<string>()));

        var result = await CreateService().SubscribeAsync(UserName, "eshop-pro", null, null, default);

        var enrollment = _store.Enrollments[SubscriptionEnrollment.KeyFor(UserId, "eshop-pro")];
        Assert.Equal(EnrollmentStatus.Completed, enrollment.Status);
        Assert.Equal(enrollment.BillingReference, result.Subscription.Reference);
        await _gateway.Received(1).CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepsClaimPendingWhenUnknownCreateCannotBeConfirmed()
    {
        GivenLinkedCustomer(7);
        _gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(BillingProviderException.NoResponse("Maxio did not respond while creating the subscription.", outcomeUnknown: true));
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => CreateService().SubscribeAsync(UserName, "eshop-pro", null, null, default));

        Assert.Equal(BillingFailureKind.NoResponse, ex.Kind);
        Assert.True(ex.OutcomeUnknown);
        Assert.Equal(EnrollmentStatus.Pending, _store.Enrollments[SubscriptionEnrollment.KeyFor(UserId, "eshop-pro")].Status);
    }

    [Fact]
    public async Task ReleasesClaimWhenMaxioRejectsCreate()
    {
        GivenLinkedCustomer(7);
        _gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException(BillingFailureKind.Rejected, "Maxio rejected the request.", 422,
                new[] { "Product is archived" }));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => CreateService().SubscribeAsync(UserName, "eshop-pro", null, null, default));

        Assert.Equal(BillingFailureKind.Rejected, ex.Kind);
        Assert.Empty(_store.Enrollments);
    }

    [Fact]
    public async Task TakesOverStaleClaimAndSettlesItWithoutSecondCreate()
    {
        GivenLinkedCustomer(7);
        var stale = new SubscriptionEnrollment(UserId, "eshop-pro", _clock.GetUtcNow().AddMinutes(-5));
        _store.Enrollments[stale.Id] = stale;
        _gateway.FindSubscriptionByReferenceAsync(stale.BillingReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(stale.BillingReference));

        var result = await CreateService().SubscribeAsync(UserName, "eshop-pro", null, null, default);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(EnrollmentStatus.Completed, stale.Status);
        await _gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default);
    }

    [Fact]
    public async Task RecreatesWithSameReferenceWhenStaleClaimNeverLanded()
    {
        GivenLinkedCustomer(7);
        var stale = new SubscriptionEnrollment(UserId, "eshop-pro", _clock.GetUtcNow().AddMinutes(-5));
        _store.Enrollments[stale.Id] = stale;
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null);

        var result = await CreateService().SubscribeAsync(UserName, "eshop-pro", null, null, default);

        Assert.False(result.AlreadySubscribed);
        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewBillingSubscription>(s => s.Reference == stale.BillingReference), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsesExistingCustomerWhenReferenceAlreadyTaken()
    {
        _gateway.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingCustomerAccount?)null, new BillingCustomerAccount(11, "eshop-user-user-1"));
        _gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException(BillingFailureKind.Rejected, "Maxio rejected the request.", 422,
                new[] { "Reference must be unique" }));

        await CreateService().SubscribeAsync(UserName, "eshop-pro", null, null, default);

        Assert.Equal(11, _store.Customers[UserId].BillingCustomerId);
        await _gateway.Received(1).CreateSubscriptionAsync(Arg.Is<NewBillingSubscription>(s => s.CustomerId == 11),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettlesUnknownCustomerCreateByReference()
    {
        _gateway.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingCustomerAccount?)null, new BillingCustomerAccount(12, "eshop-user-user-1"));
        _gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(BillingProviderException.NoResponse("Maxio did not respond while creating the customer.", outcomeUnknown: true));

        await CreateService().SubscribeAsync(UserName, "eshop-pro", null, null, default);

        Assert.Equal(12, _store.Customers[UserId].BillingCustomerId);
        await _gateway.Received(1).CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepsCustomerClaimWhenUnknownCustomerCreateCannotBeConfirmed()
    {
        _gateway.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingCustomerAccount?)null);
        _gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(BillingProviderException.NoResponse("Maxio did not respond while creating the customer.", outcomeUnknown: true));

        await Assert.ThrowsAsync<BillingProviderException>(
            () => CreateService().SubscribeAsync(UserName, "eshop-pro", null, null, default));

        Assert.False(_store.Customers[UserId].IsLinked); // kept: the next attempt looks the customer up first
        Assert.Empty(_store.Enrollments);                 // no subscription create was sent
        await _gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default);
    }

    [Fact]
    public async Task ReleasesBothClaimsWhenMaxioIsDownBeforeAnyWrite()
    {
        _gateway.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(BillingProviderException.NoResponse("Maxio did not respond while looking up the customer."));

        await Assert.ThrowsAsync<BillingProviderException>(
            () => CreateService().SubscribeAsync(UserName, "eshop-pro", null, null, default));

        Assert.Empty(_store.Customers);
        Assert.Empty(_store.Enrollments);
    }

    [Fact]
    public async Task CutsOffHungMaxioWithinBudgetAndSaysMaxioDidNotRespond()
    {
        _gateway.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(async ci =>
        {
            await Task.Delay(Timeout.Infinite, ci.Arg<CancellationToken>());
            return new SubscriptionPlanCatalog(Array.Empty<SubscriptionPlan>(), false);
        });
        var stopwatch = Stopwatch.StartNew();

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => CreateService(TimeSpan.FromMilliseconds(200)).SubscribeAsync(UserName, "eshop-pro", null, null, default));

        Assert.Equal(BillingFailureKind.NoResponse, ex.Kind);
        Assert.Contains("Maxio did not respond", ex.Message);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void DefaultBudgetStaysUnderThirtySeconds()
    {
        Assert.True(SubscriptionService.DefaultBillingBudget < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task MySubscriptionsReadsMaxioAndSettlesStalePendingClaim()
    {
        GivenLinkedCustomer(7);
        var stale = new SubscriptionEnrollment(UserId, "basic-plan", _clock.GetUtcNow().AddMinutes(-5));
        _store.Enrollments[stale.Id] = stale;
        var fresh = new SubscriptionEnrollment(UserId, "other-plan", _clock.GetUtcNow());
        _store.Enrollments[fresh.Id] = fresh;
        _gateway.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription("eshop-sub-existing") });
        _gateway.FindSubscriptionByReferenceAsync(stale.BillingReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(stale.BillingReference));
        _gateway.FindSubscriptionByReferenceAsync(fresh.BillingReference, Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null);

        var mine = await CreateService().GetMySubscriptionsAsync(UserName, default);

        Assert.Equal(2, mine.Subscriptions.Count);
        Assert.Equal(EnrollmentStatus.Completed, stale.Status);
        var pending = Assert.Single(mine.Pending);
        Assert.Equal("other-plan", pending.PlanHandle);
    }

    [Fact]
    public async Task MySubscriptionsIsEmptyForShopperWithoutBillingAccount()
    {
        var mine = await CreateService().GetMySubscriptionsAsync(UserName, default);

        Assert.Empty(mine.Subscriptions);
        Assert.Empty(mine.Pending);
        await _gateway.DidNotReceiveWithAnyArgs().ListCustomerSubscriptionsAsync(default, default);
    }

    [Fact]
    public async Task UnknownShopperIsRejected()
    {
        await Assert.ThrowsAsync<ShopperNotFoundException>(
            () => CreateService().GetMySubscriptionsAsync("nobody@example.com", default));
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public ManualClock(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
