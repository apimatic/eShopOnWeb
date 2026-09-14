using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class SubscriptionServiceTests
{
    [Fact]
    public async Task RepeatedSubscribeCreatesOneCustomerAndOneSubscription()
    {
        await using var context = CreateContext();
        var gateway = Substitute.For<ISubscriptionBillingGateway>();
        var identity = new BillingCustomerIdentity("user-1", "shopper@example.com", "shopper", "Customer");
        var plan = new SubscriptionPlan("pro", "Pro", "Pro plan", 29900, 1, "month");
        var customer = new BillingCustomer(41, "customer-reference");
        var subscription = new BillingSubscription(
            82,
            customer.Id,
            plan.Handle,
            plan.Name,
            plan.PriceInCents,
            plan.Interval,
            plan.IntervalUnit,
            "active",
            DateTimeOffset.UtcNow.AddMonths(1),
            DateTimeOffset.UtcNow);

        gateway.FindPlanAsync(plan.Handle, Arg.Any<CancellationToken>()).Returns(plan);
        gateway.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        gateway.CreateCustomerAsync(identity, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(customer);
        gateway.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null, subscription);
        gateway.CreateSubscriptionAsync(customer.Id, plan.Handle, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(subscription);

        var service = new SubscriptionService(
            context,
            gateway,
            new SubscriptionOperationLock(),
            TimeProvider.System);

        var first = await service.SubscribeAsync(identity, plan.Handle, CancellationToken.None);
        var second = await service.SubscribeAsync(identity, plan.Handle, CancellationToken.None);

        Assert.NotNull(first);
        Assert.True(first.Created);
        Assert.NotNull(second);
        Assert.False(second.Created);
        Assert.Equal(subscription.Id, second.Subscription.Id);
        await gateway.Received(1).CreateCustomerAsync(
            identity, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await gateway.Received(1).CreateSubscriptionAsync(
            customer.Id, plan.Handle, Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Single(await context.SubscriptionProvisioningRecords.ToListAsync());
    }

    [Fact]
    public async Task UnknownPlanDoesNotReserveOrCreateAnything()
    {
        await using var context = CreateContext();
        var gateway = Substitute.For<ISubscriptionBillingGateway>();
        gateway.FindPlanAsync("missing", Arg.Any<CancellationToken>())
            .Returns((SubscriptionPlan?)null);
        var service = new SubscriptionService(
            context,
            gateway,
            new SubscriptionOperationLock(),
            TimeProvider.System);

        var result = await service.SubscribeAsync(
            new BillingCustomerIdentity("user-1", "shopper@example.com", "shopper", "Customer"),
            "missing",
            CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(await context.SubscriptionProvisioningRecords.ToListAsync());
        await gateway.DidNotReceiveWithAnyArgs().CreateCustomerAsync(default!, default!, default);
        await gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task ConcurrentSubscribeCallsCreateOnlyOneSubscription()
    {
        await using var context = CreateContext();
        var gateway = Substitute.For<ISubscriptionBillingGateway>();
        var identity = new BillingCustomerIdentity("user-2", "another@example.com", "another", "Customer");
        var plan = new SubscriptionPlan("basic", "Basic", "Basic plan", 2900, 1, "month");
        var customer = new BillingCustomer(51, "customer-reference");
        var subscription = new BillingSubscription(
            92,
            customer.Id,
            plan.Handle,
            plan.Name,
            plan.PriceInCents,
            plan.Interval,
            plan.IntervalUnit,
            "active",
            DateTimeOffset.UtcNow.AddMonths(1),
            DateTimeOffset.UtcNow);
        var subscriptionLookupCount = 0;

        gateway.FindPlanAsync(plan.Handle, Arg.Any<CancellationToken>()).Returns(plan);
        gateway.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        gateway.CreateCustomerAsync(identity, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(customer);
        gateway.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref subscriptionLookupCount) == 1
                ? Task.FromResult<BillingSubscription?>(null)
                : Task.FromResult<BillingSubscription?>(subscription));
        gateway.CreateSubscriptionAsync(customer.Id, plan.Handle, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(50);
                return subscription;
            });

        var service = new SubscriptionService(
            context,
            gateway,
            new SubscriptionOperationLock(),
            TimeProvider.System);

        var results = await Task.WhenAll(
            service.SubscribeAsync(identity, plan.Handle, CancellationToken.None),
            service.SubscribeAsync(identity, plan.Handle, CancellationToken.None));

        Assert.All(results, Assert.NotNull);
        Assert.Single(results, result => result!.Created);
        await gateway.Received(1).CreateSubscriptionAsync(
            customer.Id, plan.Handle, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static CatalogContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CatalogContext(options);
    }
}
