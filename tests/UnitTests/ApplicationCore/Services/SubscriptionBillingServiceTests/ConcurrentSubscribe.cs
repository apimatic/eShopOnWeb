using Microsoft.eShopWeb.ApplicationCore.Billing;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class ConcurrentSubscribe
{
    [Fact]
    public async Task CreatesOneCustomerAndOneSubscriptionForTwoConcurrentRequests()
    {
        var gateway = Substitute.For<IMaxioBillingGateway>();
        var store = Substitute.For<ISubscriptionEnrollmentStore>();
        var shopper = new ShopperIdentity("user-1", "shopper@example.test", "Shopper", "Test");
        var plan = new BillingPlan("eshop-pro", "Pro", null, 29900, 1, "month", "default");
        var customer = new BillingCustomer(7, "customer-reference");
        var subscription = new BillingSubscription(
            11,
            "subscription-reference",
            "eshop-pro",
            "Pro",
            29900,
            "USD",
            "active",
            DateTimeOffset.UtcNow.AddMonths(1));

        gateway.FindPlanAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(plan);
        gateway.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        gateway.CreateCustomerAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(customer);
        gateway.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null, subscription);
        gateway.CreateSubscriptionAsync(
                "eshop-pro",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(50);
                return subscription;
            });

        store.AcquireAsync(
                "user-1",
                "eshop-pro",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(
                new EnrollmentLease(Guid.NewGuid(), "owner-1", EnrollmentLeaseStatus.Acquired, ReconciliationTarget.None, null, null),
                new EnrollmentLease(Guid.NewGuid(), "owner-2", EnrollmentLeaseStatus.Confirmed, ReconciliationTarget.None, 7, null));

        var service = new SubscriptionBillingService(gateway, store);

        var results = await Task.WhenAll(
            service.SubscribeAsync(shopper, "eshop-pro", CancellationToken.None),
            service.SubscribeAsync(shopper, "eshop-pro", CancellationToken.None));

        Assert.Contains(results, x => x.Outcome == SubscribeOutcome.Created);
        Assert.Contains(results, x => x.Outcome == SubscribeOutcome.Existing);
        await gateway.Received(1).CreateCustomerAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await gateway.Received(1).CreateSubscriptionAsync(
            "eshop-pro",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}
