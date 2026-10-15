using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using static Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests.SubscriptionServiceTestContext;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class SubscriptionServiceTests
{
    private readonly SubscriptionServiceTestContext _ctx = new();

    [Fact]
    public async Task Subscribe_NewBuyer_CreatesCustomerWithStableReferenceThenSubscription()
    {
        _ctx.Gateway.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BillingCustomerRecord?)null);
        _ctx.Gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomerRecord(BillingCustomerId, BillingCustomer.ReferenceFor(BuyerId)));
        _ctx.Gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>())
            .Returns(call => Subscription(900, call.Arg<NewBillingSubscription>().Reference));

        var result = await _ctx.CreateService().SubscribeAsync(Command());

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(900, result.Subscription!.Id);
        await _ctx.Gateway.Received(1).CreateCustomerAsync(
            Arg.Is<NewBillingCustomer>(c => c.Reference == "eshop-user-" + BuyerId && c.Email == Email && c.FirstName == "shopper"),
            Arg.Any<CancellationToken>());
        await _ctx.Gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewBillingSubscription>(s => s.PlanHandle == PlanHandle && s.BillingCustomerId == BillingCustomerId && s.Reference == result.SubscriptionReference),
            Arg.Any<CancellationToken>());
        Assert.Equal(BillingCustomerId, _ctx.Store.Customers[BuyerId].BillingCustomerId);
        var enrollment = _ctx.Store.Enrollments[(BuyerId, PlanHandle)];
        Assert.Equal(EnrollmentStatus.Active, enrollment.Status);
        Assert.Equal(900, enrollment.BillingSubscriptionId);
    }

    [Fact]
    public async Task Subscribe_PlanNotInConfiguredFamily_IsRejectedBeforeAnyWrite()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => _ctx.CreateService().SubscribeAsync(Command("api-call")));

        Assert.Empty(_ctx.Store.Enrollments);
        await _ctx.Gateway.DidNotReceiveWithAnyArgs().CreateCustomerAsync(default!, default);
        await _ctx.Gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default);
    }

    [Fact]
    public async Task Subscribe_WhileSameClaimInFlight_IsRefusedWithoutReachingTheProvider()
    {
        _ctx.WithRecordedCustomer();
        var inFlight = new SubscriptionEnrollment(BuyerId, PlanHandle, "eshop-sub-inflight", _ctx.Clock.GetUtcNow());
        _ctx.Store.Enrollments[(BuyerId, PlanHandle)] = inFlight;

        await Assert.ThrowsAsync<SubscriptionInProgressException>(() => _ctx.CreateService().SubscribeAsync(Command()));

        await _ctx.Gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default);
    }

    [Fact]
    public async Task Subscribe_PlanAlreadyHeld_ReturnsExistingSubscription()
    {
        _ctx.WithRecordedCustomer();
        var active = new SubscriptionEnrollment(BuyerId, PlanHandle, "eshop-sub-held", _ctx.Clock.GetUtcNow());
        active.MarkActive(777, _ctx.Clock.GetUtcNow());
        _ctx.Store.Enrollments[(BuyerId, PlanHandle)] = active;
        _ctx.Gateway.FindSubscriptionByReferenceAsync("eshop-sub-held", Arg.Any<CancellationToken>()).Returns(Subscription(777, "eshop-sub-held"));

        var result = await _ctx.CreateService().SubscribeAsync(Command());

        Assert.Equal(SubscribeOutcome.Existing, result.Outcome);
        Assert.Equal(777, result.Subscription!.Id);
        await _ctx.Gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default);
    }

    [Fact]
    public async Task Subscribe_PlanHeldButCanceled_SubscribesAgain()
    {
        _ctx.WithRecordedCustomer();
        var old = new SubscriptionEnrollment(BuyerId, PlanHandle, "eshop-sub-old", _ctx.Clock.GetUtcNow());
        old.MarkActive(777, _ctx.Clock.GetUtcNow());
        _ctx.Store.Enrollments[(BuyerId, PlanHandle)] = old;
        _ctx.Gateway.FindSubscriptionByReferenceAsync("eshop-sub-old", Arg.Any<CancellationToken>()).Returns(Subscription(777, "eshop-sub-old", "canceled"));
        _ctx.Gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>())
            .Returns(call => Subscription(778, call.Arg<NewBillingSubscription>().Reference));

        var result = await _ctx.CreateService().SubscribeAsync(Command());

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(778, result.Subscription!.Id);
    }

    [Fact]
    public async Task CreateSubscription_ConnectionFails_SettlesFromProviderLookup()
    {
        _ctx.WithRecordedCustomer();
        _ctx.Gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingOutcomeUnknownException("The billing system could not be reached."));
        _ctx.Gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Subscription(901, call.Arg<string>()));

        var result = await _ctx.CreateService().SubscribeAsync(Command());

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(901, result.Subscription!.Id);
        await _ctx.Gateway.Received(1).FindSubscriptionByReferenceAsync(result.SubscriptionReference, Arg.Any<CancellationToken>());
        Assert.Equal(EnrollmentStatus.Active, _ctx.Store.Enrollments[(BuyerId, PlanHandle)].Status);
    }

    [Fact]
    public async Task CreateSubscription_ConnectionFails_LookupAlsoFails_RecordsUnknownAndReturnsPending()
    {
        _ctx.WithRecordedCustomer();
        _ctx.Gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingOutcomeUnknownException("The billing system could not be reached."));
        _ctx.Gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderUnavailableException("The billing system could not be reached."));

        var result = await _ctx.CreateService().SubscribeAsync(Command());

        Assert.Equal(SubscribeOutcome.Pending, result.Outcome);
        var enrollment = _ctx.Store.Enrollments[(BuyerId, PlanHandle)];
        Assert.Equal(EnrollmentStatus.Unknown, enrollment.Status);
        Assert.Equal(result.SubscriptionReference, enrollment.SubscriptionReference);
    }

    [Fact]
    public async Task UnknownEnrollment_IsSettledByMySubscriptions()
    {
        _ctx.WithRecordedCustomer();
        var unknown = new SubscriptionEnrollment(BuyerId, PlanHandle, "eshop-sub-unknown", _ctx.Clock.GetUtcNow());
        unknown.MarkUnknown(_ctx.Clock.GetUtcNow());
        _ctx.Store.Enrollments[(BuyerId, PlanHandle)] = unknown;
        _ctx.Gateway.FindSubscriptionByReferenceAsync("eshop-sub-unknown", Arg.Any<CancellationToken>()).Returns(Subscription(902, "eshop-sub-unknown"));
        _ctx.Gateway.ListCustomerSubscriptionsAsync(BillingCustomerId, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(902, "eshop-sub-unknown") });

        var mine = await _ctx.CreateService().GetMySubscriptionsAsync(BuyerId);

        Assert.Single(mine.Subscriptions);
        Assert.Empty(mine.Pending);
        Assert.Equal(EnrollmentStatus.Active, unknown.Status);
        Assert.Equal(902, unknown.BillingSubscriptionId);
    }

    [Fact]
    public async Task StaleUnknownEnrollment_NotFoundAtProvider_IsReleasedAndSubscribedAgain()
    {
        _ctx.WithRecordedCustomer();
        var unknown = new SubscriptionEnrollment(BuyerId, PlanHandle, "eshop-sub-lost", _ctx.Clock.GetUtcNow());
        unknown.MarkUnknown(_ctx.Clock.GetUtcNow());
        _ctx.Store.Enrollments[(BuyerId, PlanHandle)] = unknown;
        _ctx.Clock.Advance(SubscriptionService.ClaimStaleAfter + TimeSpan.FromSeconds(1));
        _ctx.Gateway.FindSubscriptionByReferenceAsync("eshop-sub-lost", Arg.Any<CancellationToken>()).Returns((BillingSubscription?)null);
        _ctx.Gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>())
            .Returns(call => Subscription(903, call.Arg<NewBillingSubscription>().Reference));

        var result = await _ctx.CreateService().SubscribeAsync(Command());

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.NotEqual("eshop-sub-lost", result.SubscriptionReference);
        await _ctx.Gateway.Received(1).CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateSubscription_Rejected_ReleasesClaimAndRethrows()
    {
        _ctx.WithRecordedCustomer();
        _ctx.Gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingRequestRejectedException("rejected", new[] { "No payment method" }, 422));

        await Assert.ThrowsAsync<BillingRequestRejectedException>(() => _ctx.CreateService().SubscribeAsync(Command()));

        Assert.Empty(_ctx.Store.Enrollments);
    }

    [Fact]
    public async Task CreateCustomer_ConnectionFails_SettlesFromReferenceLookup()
    {
        var reference = BillingCustomer.ReferenceFor(BuyerId);
        _ctx.Gateway.FindCustomerByReferenceAsync(reference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomerRecord?)null, new BillingCustomerRecord(BillingCustomerId, reference));
        _ctx.Gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingOutcomeUnknownException("The billing system could not be reached."));
        _ctx.Gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>())
            .Returns(call => Subscription(904, call.Arg<NewBillingSubscription>().Reference));

        var result = await _ctx.CreateService().SubscribeAsync(Command());

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        await _ctx.Gateway.Received(1).CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>());
        await _ctx.Gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewBillingSubscription>(s => s.BillingCustomerId == BillingCustomerId), Arg.Any<CancellationToken>());
        Assert.Equal(BillingCustomerId, _ctx.Store.Customers[BuyerId].BillingCustomerId);
    }

    [Fact]
    public async Task CreateCustomer_ConnectionFailsAndNotFound_ReleasesBothClaims()
    {
        _ctx.Gateway.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BillingCustomerRecord?)null);
        _ctx.Gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingOutcomeUnknownException("The billing system could not be reached."));

        await Assert.ThrowsAsync<BillingProviderUnavailableException>(() => _ctx.CreateService().SubscribeAsync(Command()));

        Assert.Empty(_ctx.Store.Customers);
        Assert.Empty(_ctx.Store.Enrollments);
        await _ctx.Gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default);
    }

    [Fact]
    public async Task CreateCustomer_RejectedBecauseReferenceTaken_AdoptsTheExistingCustomer()
    {
        var reference = BillingCustomer.ReferenceFor(BuyerId);
        _ctx.Gateway.FindCustomerByReferenceAsync(reference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomerRecord?)null, new BillingCustomerRecord(BillingCustomerId, reference));
        _ctx.Gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingRequestRejectedException("rejected", new[] { "Reference must be unique" }, 422));
        _ctx.Gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>())
            .Returns(call => Subscription(905, call.Arg<NewBillingSubscription>().Reference));

        var result = await _ctx.CreateService().SubscribeAsync(Command());

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(BillingCustomerId, _ctx.Store.Customers[BuyerId].BillingCustomerId);
    }

    [Fact]
    public async Task MySubscriptions_WithoutCustomer_IsEmptyAndMakesNoProviderCall()
    {
        var mine = await _ctx.CreateService().GetMySubscriptionsAsync(BuyerId);

        Assert.Empty(mine.Subscriptions);
        Assert.Empty(mine.Pending);
        await _ctx.Gateway.DidNotReceiveWithAnyArgs().ListCustomerSubscriptionsAsync(default, default);
    }
}
