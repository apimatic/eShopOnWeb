using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Subscribe
{
    private const string USER_REFERENCE = "shopper@example.com";
    private const string PLAN_HANDLE = "eshop-pro";

    private readonly ISubscriptionBillingClient _mockBilling = Substitute.For<ISubscriptionBillingClient>();
    private readonly IAppLogger<SubscriptionService> _mockLogger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly SubscriptionService _service;

    public Subscribe()
    {
        _service = new SubscriptionService(_mockBilling, _mockLogger);

        _mockBilling.GetPlansAsync(default).Returns(new List<BillingPlan> { ProPlan() });
    }

    private static BillingPlan ProPlan() => new()
    {
        Id = 7130999,
        Handle = PLAN_HANDLE,
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
        RequiresPaymentProfile = false,
    };

    private static BillingCustomer ExistingCustomer() => new()
    {
        Id = 42,
        Reference = USER_REFERENCE,
        FirstName = "Shop",
        LastName = "Per",
        Email = USER_REFERENCE,
    };

    private static BillingSubscription Subscription(int id = 555, string state = "active", string planHandle = PLAN_HANDLE) => new()
    {
        Id = id,
        State = state,
        CustomerId = 42,
        PlanHandle = planHandle,
        PlanName = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
        PaymentCollectionMethod = "remittance",
        CurrentPeriodStartsAt = DateTimeOffset.UtcNow,
        CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
        NextBillingDate = DateTimeOffset.UtcNow.AddMonths(1),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static SubscribeRequest Request(string planHandle = PLAN_HANDLE, string? idempotencyKey = null) =>
        new(USER_REFERENCE, USER_REFERENCE, null, null, planHandle, idempotencyKey);

    [Fact]
    public async Task ProvisionsBillingCustomerAndCreatesSubscription_WhenShopperHasNoBillingCustomer()
    {
        var customer = ExistingCustomer();
        _mockBilling.FindCustomerByReferenceAsync(USER_REFERENCE, default).Returns((BillingCustomer?)null, customer);
        _mockBilling.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), default).Returns(customer);
        _mockBilling.GetSubscriptionsForCustomerAsync(42, default).Returns(new List<BillingSubscription>());
        var created = Subscription();
        _mockBilling.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), default).Returns(created);

        var outcome = await _service.SubscribeAsync(Request());

        Assert.True(outcome.WasNewlyCreated);
        Assert.Equal(created.Id, outcome.Subscription.Id);
        await _mockBilling.Received(1).CreateCustomerAsync(
            Arg.Is<NewBillingCustomer>(c => c.Reference == USER_REFERENCE && c.Email == USER_REFERENCE), default);
        await _mockBilling.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewBillingSubscription>(s => s.ProductHandle == PLAN_HANDLE && s.CustomerId == 42 && s.PaymentCollectionMethod == "remittance"), default);
    }

    [Fact]
    public async Task ReturnsExistingSubscription_WhenShopperAlreadySubscribedToThePlan()
    {
        var existing = Subscription(999);
        _mockBilling.FindCustomerByReferenceAsync(USER_REFERENCE, default).Returns(ExistingCustomer());
        _mockBilling.GetSubscriptionsForCustomerAsync(42, default).Returns(new List<BillingSubscription> { existing });

        var outcome = await _service.SubscribeAsync(Request());

        Assert.False(outcome.WasNewlyCreated);
        Assert.Equal(999, outcome.Subscription.Id);
        await _mockBilling.DidNotReceive().CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), default);
        await _mockBilling.DidNotReceive().CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), default);
    }

    [Fact]
    public async Task CreatesNewSubscription_WhenPreviousSubscriptionWasCanceled()
    {
        _mockBilling.FindCustomerByReferenceAsync(USER_REFERENCE, default).Returns(ExistingCustomer());
        _mockBilling.GetSubscriptionsForCustomerAsync(42, default).Returns(new List<BillingSubscription>
        {
            Subscription(111, state: "canceled"),
        });
        var created = Subscription(222);
        _mockBilling.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), default).Returns(created);

        var outcome = await _service.SubscribeAsync(Request());

        Assert.True(outcome.WasNewlyCreated);
        Assert.Equal(222, outcome.Subscription.Id);
    }

    [Fact]
    public async Task CreatesNewSubscription_WhenShopperIsSubscribedToADifferentPlan()
    {
        _mockBilling.FindCustomerByReferenceAsync(USER_REFERENCE, default).Returns(ExistingCustomer());
        _mockBilling.GetSubscriptionsForCustomerAsync(42, default).Returns(new List<BillingSubscription>
        {
            Subscription(112, planHandle: "basic-plan"),
        });
        var created = Subscription(223);
        _mockBilling.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), default).Returns(created);

        var outcome = await _service.SubscribeAsync(Request());

        Assert.True(outcome.WasNewlyCreated);
        Assert.Equal(223, outcome.Subscription.Id);
    }

    [Fact]
    public async Task ThrowsPlanNotFound_WhenPlanIsNotInTheCatalog()
    {
        _mockBilling.FindCustomerByReferenceAsync(USER_REFERENCE, default).Returns(ExistingCustomer());

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(() => _service.SubscribeAsync(Request("no-such-plan")));

        Assert.Equal(SubscriptionBillingErrorKind.PlanNotFound, exception.Kind);
    }

    [Fact]
    public async Task ThrowsRejected_WhenPlanRequiresAPaymentMethodTheFlowDoesNotCollect()
    {
        _mockBilling.GetPlansAsync(default).Returns(new List<BillingPlan>
        {
            new() { Id = 1, Handle = PLAN_HANDLE, Name = "Card Plan", PriceInCents = 100, RequiresPaymentProfile = true },
        });

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(() => _service.SubscribeAsync(Request()));

        Assert.Equal(SubscriptionBillingErrorKind.Rejected, exception.Kind);
        await _mockBilling.DidNotReceive().CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), default);
    }

    [Fact]
    public async Task RecoversFromCustomerCreateConflict_WhenConcurrentRequestWonTheRace()
    {
        var customer = ExistingCustomer();
        _mockBilling.FindCustomerByReferenceAsync(USER_REFERENCE, default)
            .Returns((BillingCustomer?)null, customer);
        _mockBilling.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), default)
            .ThrowsAsync(new SubscriptionBillingException(SubscriptionBillingErrorKind.Rejected, "Reference has already been taken"));
        _mockBilling.GetSubscriptionsForCustomerAsync(42, default).Returns(new List<BillingSubscription>());
        _mockBilling.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), default).Returns(Subscription(333));

        var outcome = await _service.SubscribeAsync(Request());

        Assert.Equal(333, outcome.Subscription.Id);
    }

    [Fact]
    public async Task ForwardsCallerIdempotencyKeyToTheBillingClient()
    {
        _mockBilling.FindCustomerByReferenceAsync(USER_REFERENCE, default).Returns(ExistingCustomer());
        _mockBilling.GetSubscriptionsForCustomerAsync(42, default).Returns(new List<BillingSubscription>());
        _mockBilling.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), default).Returns(Subscription(444));

        await _service.SubscribeAsync(Request(idempotencyKey: "caller-key-1"));

        await _mockBilling.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewBillingSubscription>(s => s.IdempotencyKey == "caller-key-1"), default);
    }

    [Fact]
    public async Task DerivesFallbackCustomerNames_FromEmailWhenRequestOmitsThem()
    {
        NewBillingCustomer? captured = null;
        var customer = ExistingCustomer();
        _mockBilling.FindCustomerByReferenceAsync("jane.doe@example.com", default).Returns((BillingCustomer?)null, customer);
        _mockBilling.CreateCustomerAsync(Arg.Do<NewBillingCustomer>(c => captured = c), default).Returns(customer);
        _mockBilling.GetSubscriptionsForCustomerAsync(42, default).Returns(new List<BillingSubscription>());
        _mockBilling.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), default).Returns(Subscription(555));

        await _service.SubscribeAsync(new SubscribeRequest("jane.doe@example.com", "jane.doe@example.com", "  ", null, PLAN_HANDLE, null));

        Assert.NotNull(captured);
        Assert.Equal("jane.doe", captured!.FirstName);
        Assert.Equal("Shopper", captured.LastName);
    }
}
