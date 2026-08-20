using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class SubscriptionServiceTests
{
    private static readonly BillingPlan Plan = new("pro", "Pro", null, 29900, 1, "month", false);
    private static readonly BillingCustomer Customer = new(42, "customer-reference");
    private static readonly BillingSubscription Subscription = new(
        99, "subscription-reference", "active", "pro", "Pro", "family", 29900, 1, "month", "USD",
        DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow.AddMonths(1));

    [Fact]
    public async Task ConcurrentDuplicateRequestsCreateOnlyOneMaxioSubscription()
    {
        var gateway = Substitute.For<ISubscriptionBillingGateway>();
        var repository = Substitute.For<IRepository<SubscriptionLink>>();
        SubscriptionLink? storedLink = null;
        BillingSubscription? storedSubscription = null;

        gateway.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new[] { Plan });
        gateway.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Customer);
        gateway.ListSubscriptionsAsync(Customer.Id, Arg.Any<CancellationToken>())
            .Returns(_ => storedSubscription is null
                ? Array.Empty<BillingSubscription>()
                : new[] { storedSubscription });
        gateway.CreateSubscriptionAsync(Customer.Id, Plan.Handle, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                storedSubscription = Subscription;
                return Subscription;
            });
        repository.FirstOrDefaultAsync(
                Arg.Any<ISpecification<SubscriptionLink>>(), Arg.Any<CancellationToken>())
            .Returns(_ => storedLink);
        repository.AddAsync(Arg.Any<SubscriptionLink>(), Arg.Any<CancellationToken>())
            .Returns(call => storedLink = call.Arg<SubscriptionLink>());

        var service = new SubscriptionService(gateway, repository, new SingleKeyOperationLock());
        var shopper = new ShopperIdentity("user-1", "shopper@example.com");

        var results = await Task.WhenAll(
            service.SubscribeAsync(shopper, Plan.Handle, CancellationToken.None),
            service.SubscribeAsync(shopper, Plan.Handle, CancellationToken.None));

        Assert.All(results, result => Assert.Equal(Subscription.Id, result.Id));
        await gateway.Received(1).CreateSubscriptionAsync(
            Customer.Id, Plan.Handle, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CustomerCreateConflictIsRecoveredByReferenceLookup()
    {
        var gateway = Substitute.For<ISubscriptionBillingGateway>();
        var repository = Substitute.For<IRepository<SubscriptionLink>>();
        var lookupCount = 0;
        gateway.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new[] { Plan });
        gateway.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++lookupCount == 1 ? null : Customer);
        gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns<BillingCustomer>(_ => throw new BillingProviderException("conflict", 422));
        gateway.ListSubscriptionsAsync(Customer.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription });

        var service = new SubscriptionService(gateway, repository, new SingleKeyOperationLock());
        var result = await service.SubscribeAsync(
            new ShopperIdentity("user-1", "shopper@example.com"), Plan.Handle, CancellationToken.None);

        Assert.Equal(Subscription.Id, result.Id);
        await gateway.Received(2).FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await gateway.Received(1).CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>());
    }

    private sealed class SingleKeyOperationLock : ISubscriptionOperationLock
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public async ValueTask<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);
            return new Lease(_semaphore);
        }

        private sealed class Lease : IAsyncDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            public Lease(SemaphoreSlim semaphore) => _semaphore = semaphore;
            public ValueTask DisposeAsync()
            {
                _semaphore.Release();
                return ValueTask.CompletedTask;
            }
        }
    }
}
