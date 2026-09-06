using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using NSubstitute;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

/// <summary>Shared setup for the subscribe-flow tests.</summary>
public abstract class SubscriptionServiceFixture
{
    protected const string PlanHandle = "eshop-pro";
    protected const string UserName = "demouser@microsoft.com";
    protected const long CustomerId = 4242;

    protected readonly ISubscriptionBillingGateway Gateway = Substitute.For<ISubscriptionBillingGateway>();
    protected readonly IAppLogger<SubscriptionService> Logger = Substitute.For<IAppLogger<SubscriptionService>>();

    protected SubscriberIdentity Subscriber { get; } = new(UserName, UserName);

    /// <summary>Frozen so the retry-window bucket cannot roll over mid-test.</summary>
    protected FrozenTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero));

    protected SubscriptionService CreateService() => new(Gateway, new KeyedAsyncLock(), Logger, Clock);

    protected static SubscriptionPlan Plan(string handle = PlanHandle, long priceInCents = 29900) => new()
    {
        Handle = handle,
        Name = "Pro Plan",
        PriceInCents = priceInCents,
        Currency = "USD",
        Interval = 1,
        IntervalUnit = "month",
        PaymentMethodRequired = false,
        ProductFamilyHandle = "eshop-subscribe"
    };

    protected static BillingCustomer Customer(long id = CustomerId) => new()
    {
        Id = id,
        Reference = BillingReferences.ForUser(UserName),
        Email = UserName
    };

    protected static CustomerSubscription Subscription(
        long id = 1,
        string state = "active",
        string planHandle = PlanHandle) => new()
    {
        Id = id,
        State = state,
        PlanHandle = planHandle,
        PlanName = "Pro Plan",
        PriceInCents = 29900,
        Currency = "USD",
        CreatedAt = DateTimeOffset.UtcNow,
        CustomerId = CustomerId
    };

    protected void GivenPlanExists(SubscriptionPlan? plan = null) =>
        Gateway.FindPlanAsync(PlanHandle, Arg.Any<System.Threading.CancellationToken>()).Returns(plan ?? Plan());

    protected void GivenCustomerExists(BillingCustomer? customer = null) =>
        Gateway.FindCustomerByReferenceAsync(BillingReferences.ForUser(UserName), Arg.Any<System.Threading.CancellationToken>())
            .Returns(customer ?? Customer());

    protected void GivenSubscriptions(params CustomerSubscription[] subscriptions) =>
        Gateway.ListSubscriptionsAsync(CustomerId, Arg.Any<System.Threading.CancellationToken>())
            .Returns(new List<CustomerSubscription>(subscriptions));
}

/// <summary>A <see cref="TimeProvider"/> whose clock only moves when a test moves it.</summary>
public sealed class FrozenTimeProvider : TimeProvider
{
    public FrozenTimeProvider(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;
}
