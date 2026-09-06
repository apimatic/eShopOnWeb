using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

/// <summary>
/// Shared setup for the subscription service tests: a substituted billing gateway plus the real
/// subscriber lock, since serialising concurrent attempts is part of what these tests exercise.
/// </summary>
public abstract class SubscriptionServiceFixture
{
    protected const string ProPlanHandle = "eshop-pro";
    protected const string BasicPlanHandle = "basic-plan";
    protected const int CustomerId = 42;
    protected const string CustomerReference = "eshoponweb-demouser@microsoft.com";

    protected readonly IBillingGateway MockGateway = Substitute.For<IBillingGateway>();
    protected readonly IAppLogger<SubscriptionService> MockLogger = Substitute.For<IAppLogger<SubscriptionService>>();

    protected readonly SubscriberIdentity Subscriber =
        new("demouser@microsoft.com", "demouser@microsoft.com", "user-id-1");

    protected SubscriptionService CreateService(string? defaultPlanHandle = ProPlanHandle,
        string? paymentCollectionMethod = "remittance")
    {
        var options = Substitute.For<ISubscriptionOptions>();
        options.DefaultPlanHandle.Returns(defaultPlanHandle);
        options.PaymentCollectionMethod.Returns(paymentCollectionMethod);

        return new SubscriptionService(MockGateway, new SubscriberLock(), options, MockLogger);
    }

    protected static SubscriptionPlan Plan(string handle, int priceInCents = 29900, string? pricePointHandle = null) =>
        new(handle, handle, null, priceInCents, 1, "month", pricePointHandle, false, "eshop-subscribe");

    protected static BillingCustomer Customer(int id = CustomerId, string reference = CustomerReference) =>
        new(id, reference, "demouser@microsoft.com", "demouser", "eShopOnWeb");

    protected static CustomerSubscription Subscription(int id,
        string planHandle,
        string state = SubscriptionStates.Active) =>
        new(id, $"ref-{id}", state, planHandle, planHandle, 29900, 1, "month",
            null, null, null, null, null, "remittance", CustomerId, CustomerReference);

    protected void GivenPlans(params SubscriptionPlan[] plans)
    {
        MockGateway.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyCollection<SubscriptionPlan>)plans);

        MockGateway.FindPlanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Array.Find(plans,
                p => string.Equals(p.Handle, call.ArgAt<string>(0), StringComparison.OrdinalIgnoreCase)));
    }

    protected void GivenExistingCustomer(BillingCustomer? customer) =>
        MockGateway.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(customer);

    protected void GivenSubscriptions(params CustomerSubscription[] subscriptions) =>
        MockGateway.ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyCollection<CustomerSubscription>)subscriptions);
}
