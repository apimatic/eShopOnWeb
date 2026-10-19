using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Subscribe
{
    private const string BuyerId = "buyer@example.com";
    private static readonly BuyerProfile Buyer = new(BuyerId, BuyerId, null, null);
    private static readonly BillingPlan Pro = new(1, "eshop-pro", "Pro Plan", null, 29900, 1, "month", false);
    private static readonly BillingPlan Basic = new(2, "basic-plan", "Basic Plan", null, 2900, 1, "month", false);
    private static readonly BillingCustomer Customer = new(7, SubscriptionService.CustomerReferenceFor(BuyerId), BuyerId);

    private readonly ISubscriptionBillingGateway _gateway = Substitute.For<ISubscriptionBillingGateway>();
    private readonly ISubscriptionEnrollmentStore _store = Substitute.For<ISubscriptionEnrollmentStore>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly SubscriptionService _service;

    public Subscribe()
    {
        _service = new SubscriptionService(_gateway, _store, TimeProvider.System, _logger);
        _gateway.GetPlansAsync(Arg.Any<CancellationToken>())
            .Returns(new BillingPlanCatalog("eshop-subscribe", new[] { Pro, Basic }, false));
        _gateway.FindCustomerByReferenceAsync(Customer.Reference!, Arg.Any<CancellationToken>()).Returns(Customer);
        _gateway.ListCustomerSubscriptionsAsync(Customer.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<BillingSubscription>());
        _store.TryClaimAsync(Arg.Any<SubscriptionEnrollment>(), Arg.Any<CancellationToken>()).Returns((SubscriptionEnrollment?)null);
    }

    private static BillingSubscription Subscription(int id, string plan, string? reference = null, bool live = true) =>
        new(id, reference, Customer.Id, live ? "active" : "canceled", live, plan, plan, 29900, "USD", 1, "month",
            DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task UnknownPlan_IsRefusedBeforeAnyClaimOrWrite()
    {
        await Assert.ThrowsAsync<UnknownSubscriptionPlanException>(() => _service.SubscribeAsync(Buyer, "gold-plan", CancellationToken.None));

        await _store.DidNotReceiveWithAnyArgs().TryClaimAsync(default!, default);
        await _gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task NewBuyer_ClaimsBeforeCreating_AndCompletesTheClaim()
    {
        _gateway.FindCustomerByReferenceAsync(Customer.Reference!, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        _gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>()).Returns(Customer);
        _gateway.CreateSubscriptionAsync(Customer.Id, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Subscription(42, "eshop-pro", ci.ArgAt<string>(2)));

        var outcome = await _service.SubscribeAsync(Buyer, "eshop-pro", CancellationToken.None);

        Assert.True(outcome.Result!.Created);
        Assert.Equal(42, outcome.Result.Subscription.Id);
        Received.InOrder(() =>
        {
            _store.TryClaimAsync(Arg.Any<SubscriptionEnrollment>(), Arg.Any<CancellationToken>());
            _gateway.CreateCustomerAsync(Arg.Is<NewBillingCustomer>(c => c.Reference == Customer.Reference && c.Email == BuyerId), Arg.Any<CancellationToken>());
            _gateway.CreateSubscriptionAsync(Customer.Id, "eshop-pro", Arg.Is<string>(r => r.StartsWith("eshop-sub-")), Arg.Any<CancellationToken>());
        });
        await _store.Received().SaveAsync(Arg.Is<SubscriptionEnrollment>(e => e.Status == EnrollmentStatus.Active && e.BillingSubscriptionId == 42), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SecondRequest_WithActiveClaimForSamePlan_ReturnsExistingWithoutWriting()
    {
        var active = SubscriptionEnrollment.Claim(BuyerId, "eshop-pro", DateTimeOffset.UtcNow);
        active.Complete(42, "eshop-pro", DateTimeOffset.UtcNow);
        _store.TryClaimAsync(Arg.Any<SubscriptionEnrollment>(), Arg.Any<CancellationToken>()).Returns(active);
        _gateway.FindSubscriptionByReferenceAsync(active.SubscriptionReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(42, "eshop-pro", active.SubscriptionReference));

        var outcome = await _service.SubscribeAsync(Buyer, "eshop-pro", CancellationToken.None);

        Assert.False(outcome.Result!.Created);
        Assert.Equal(42, outcome.Result.Subscription.Id);
        await _gateway.DidNotReceiveWithAnyArgs().CreateCustomerAsync(default!, default);
        await _gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task ActiveClaimForAnotherPlan_IsAConflict()
    {
        var active = SubscriptionEnrollment.Claim(BuyerId, "basic-plan", DateTimeOffset.UtcNow);
        active.Complete(41, "basic-plan", DateTimeOffset.UtcNow);
        _store.TryClaimAsync(Arg.Any<SubscriptionEnrollment>(), Arg.Any<CancellationToken>()).Returns(active);

        await Assert.ThrowsAsync<SubscriptionConflictException>(() => _service.SubscribeAsync(Buyer, "eshop-pro", CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentRequest_WhileFirstIsInFlight_IsAConflictAndNeverReachesTheProvider()
    {
        var inFlight = SubscriptionEnrollment.Claim(BuyerId, "eshop-pro", DateTimeOffset.UtcNow);
        _store.TryClaimAsync(Arg.Any<SubscriptionEnrollment>(), Arg.Any<CancellationToken>()).Returns(inFlight);

        await Assert.ThrowsAsync<SubscriptionConflictException>(() => _service.SubscribeAsync(Buyer, "eshop-pro", CancellationToken.None));

        await _gateway.DidNotReceiveWithAnyArgs().FindCustomerByReferenceAsync(default!, default);
        await _gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task LiveSubscriptionAtProvider_IsAdoptedInsteadOfDuplicated()
    {
        _gateway.ListCustomerSubscriptionsAsync(Customer.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(40, "eshop-pro", live: false), Subscription(41, "eshop-pro") });

        var outcome = await _service.SubscribeAsync(Buyer, "eshop-pro", CancellationToken.None);

        Assert.False(outcome.Result!.Created);
        Assert.Equal(41, outcome.Result.Subscription.Id);
        await _gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task ProviderRefusal_ReleasesTheClaim()
    {
        _gateway.CreateSubscriptionAsync(Customer.Id, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException(BillingFailureKind.Rejected, "rejected", 422));

        await Assert.ThrowsAsync<BillingProviderException>(() => _service.SubscribeAsync(Buyer, "eshop-pro", CancellationToken.None));

        await _store.Received(1).ReleaseAsync(Arg.Any<SubscriptionEnrollment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownOutcome_KeepsTheClaimMarkedUnknown()
    {
        _gateway.CreateSubscriptionAsync(Customer.Id, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingOutcomeUnknownException("unknown"));

        var outcome = await _service.SubscribeAsync(Buyer, "eshop-pro", CancellationToken.None);

        Assert.True(outcome.OutcomeUnknown);
        await _store.DidNotReceiveWithAnyArgs().ReleaseAsync(default!, default);
        await _store.Received().SaveAsync(Arg.Is<SubscriptionEnrollment>(e => e.Status == EnrollmentStatus.OutcomeUnknown), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownClaim_IsTakenOverAndSettledByReference()
    {
        var unknown = SubscriptionEnrollment.Claim(BuyerId, "eshop-pro", DateTimeOffset.UtcNow);
        unknown.MarkOutcomeUnknown(DateTimeOffset.UtcNow);
        _store.TryClaimAsync(Arg.Any<SubscriptionEnrollment>(), Arg.Any<CancellationToken>()).Returns(unknown);
        _store.TryTakeOverAsync(unknown, "eshop-pro", Arg.Any<CancellationToken>()).Returns(true);
        _gateway.FindSubscriptionByReferenceAsync(unknown.SubscriptionReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(42, "eshop-pro", unknown.SubscriptionReference));

        var outcome = await _service.SubscribeAsync(Buyer, "eshop-pro", CancellationToken.None);

        Assert.Equal(42, outcome.Result!.Subscription.Id);
        Assert.False(outcome.Result.Created);
        await _gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task UnknownClaimWithNothingAtProvider_RetriesUnderTheSameReference()
    {
        var unknown = SubscriptionEnrollment.Claim(BuyerId, "eshop-pro", DateTimeOffset.UtcNow);
        unknown.MarkOutcomeUnknown(DateTimeOffset.UtcNow);
        _store.TryClaimAsync(Arg.Any<SubscriptionEnrollment>(), Arg.Any<CancellationToken>()).Returns(unknown);
        _store.TryTakeOverAsync(unknown, "eshop-pro", Arg.Any<CancellationToken>()).Returns(true);
        _gateway.FindSubscriptionByReferenceAsync(unknown.SubscriptionReference, Arg.Any<CancellationToken>()).Returns((BillingSubscription?)null);
        _gateway.CreateSubscriptionAsync(Customer.Id, "eshop-pro", unknown.SubscriptionReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(43, "eshop-pro", unknown.SubscriptionReference));

        var outcome = await _service.SubscribeAsync(Buyer, "eshop-pro", CancellationToken.None);

        Assert.True(outcome.Result!.Created);
        Assert.Equal(43, outcome.Result.Subscription.Id);
    }
}
