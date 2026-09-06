using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using NSubstitute;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

/// <summary>
/// Shared fixtures for the subscribe flow: a substituted billing gateway and the plans it offers.
/// </summary>
public abstract class SubscriptionServiceTestBase
{
    protected const string UserName = "demouser@microsoft.com";
    protected const string ProPlanHandle = "eshop-pro";
    protected const string BasicPlanHandle = "basic-plan";

    protected static readonly string CustomerReference = BillingCustomerReference.For(UserName);

    protected readonly IBillingGateway BillingGateway = Substitute.For<IBillingGateway>();

    protected SubscriptionService CreateService() => new(
        BillingGateway,
        new KeyedAsyncLock(),
        Substitute.For<IAppLogger<SubscriptionService>>());

    protected SubscriptionServiceTestBase()
    {
        BillingGateway.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan>
            {
                Plan(BasicPlanHandle, "Basic Plan", 2900),
                Plan(ProPlanHandle, "Pro Plan", 29900)
            });
    }

    protected static SubscriptionPlan Plan(string handle, string name, long priceInCents) => new()
    {
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Currency = "USD",
        Interval = 1,
        IntervalUnit = "month"
    };

    protected static BillingCustomer Customer(int id = 42) => new()
    {
        Id = id,
        Reference = CustomerReference,
        Email = UserName,
        FirstName = "Demouser",
        LastName = "Demouser"
    };

    protected static BillingSubscription Subscription(int id, string planHandle, string state,
        DateTimeOffset? createdAt = null) => new()
        {
            Id = id,
            PlanHandle = planHandle,
            State = state,
            CustomerId = 42,
            CustomerReference = CustomerReference,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        };
}
