using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Subscription;

public class MaxioSubscriptionServiceTests
{
    private const string ProductFamilyHandle = "eshop-subscribe";

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionWhenShopperIsNew()
    {
        string email = "new@eshop.test";
        var client = Substitute.For<IMaxioBillingClient>();
        client.FindCustomerByReferenceAsync(email, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MaxioCustomer?>(null));

        var customer = new MaxioCustomer { Id = 55, Email = email, Reference = email };
        client.CreateCustomerAsync("New", "Eshop", email, email, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(customer));
        client.ListCustomerSubscriptionsAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MaxioSubscription>>(new List<MaxioSubscription>()));
        client.GetSiteAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MaxioSite { RelationshipInvoicingEnabled = true }));

        var created = NewSubscription(909, "eshop-pro", "active");
        client.CreateSubscriptionAsync("eshop-pro", customer.Id, "remittance", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(created));

        var service = CreateService(client);
        var result = await service.SubscribeAsync(new SubscriberProfile(email, firstName: null, lastName: null), "eshop-pro", CancellationToken.None);

        Assert.False(result.AlreadyExisted);
        Assert.Equal(909, result.Subscription.Id);
        await client.Received(1).CreateCustomerAsync("New", "Eshop", email, email, Arg.Any<CancellationToken>());
        await client.Received(1).CreateSubscriptionAsync("eshop-pro", customer.Id, "remittance", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeReusesExistingSubscriptionForTheSamePlan()
    {
        string email = "existing@eshop.test";
        var client = Substitute.For<IMaxioBillingClient>();
        var customer = new MaxioCustomer { Id = 77, Email = email, Reference = email };
        client.FindCustomerByReferenceAsync(email, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MaxioCustomer?>(customer));

        var existing = NewSubscription(101, "eshop-pro", "active");
        client.ListCustomerSubscriptionsAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MaxioSubscription>>(new List<MaxioSubscription> { existing }));

        var service = CreateService(client);
        var result = await service.SubscribeAsync(new SubscriberProfile(email, firstName: null, lastName: null), "eshop-pro", CancellationToken.None);

        Assert.True(result.AlreadyExisted);
        Assert.Equal(101, result.Subscription.Id);
        await client.DidNotReceiveWithAnyArgs().CreateCustomerAsync(default!, default!, default!, default!, default);
        await client.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default, default!, default);
        await client.DidNotReceiveWithAnyArgs().GetSiteAsync(default);
    }

    [Fact]
    public async Task SubscribeCreatesANewSubscriptionWhenThePreviousOneWasCanceled()
    {
        string email = "former@eshop.test";
        var client = Substitute.For<IMaxioBillingClient>();
        var customer = new MaxioCustomer { Id = 88, Email = email, Reference = email };
        client.FindCustomerByReferenceAsync(email, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MaxioCustomer?>(customer));

        var canceled = NewSubscription(102, "eshop-pro", "canceled");
        client.ListCustomerSubscriptionsAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MaxioSubscription>>(new List<MaxioSubscription> { canceled }));
        client.GetSiteAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MaxioSite { RelationshipInvoicingEnabled = true }));

        var fresh = NewSubscription(103, "eshop-pro", "active");
        client.CreateSubscriptionAsync("eshop-pro", customer.Id, "remittance", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(fresh));

        var service = CreateService(client);
        var result = await service.SubscribeAsync(new SubscriberProfile(email, firstName: null, lastName: null), "eshop-pro", CancellationToken.None);

        Assert.False(result.AlreadyExisted);
        Assert.Equal(103, result.Subscription.Id);
        await client.Received(1).CreateSubscriptionAsync("eshop-pro", customer.Id, "remittance", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsesInvoiceCollectionMethodWhenSiteDoesNotUseRelationshipInvoicing()
    {
        string email = "legacy@eshop.test";
        var client = Substitute.For<IMaxioBillingClient>();
        client.FindCustomerByReferenceAsync(email, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MaxioCustomer?>(null));
        var customer = new MaxioCustomer { Id = 66, Email = email, Reference = email };
        client.CreateCustomerAsync("Legacy", "Eshop", email, email, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(customer));
        client.ListCustomerSubscriptionsAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MaxioSubscription>>(new List<MaxioSubscription>()));
        client.GetSiteAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MaxioSite { RelationshipInvoicingEnabled = false }));

        var created = NewSubscription(910, "eshop-pro", "active");
        client.CreateSubscriptionAsync("eshop-pro", customer.Id, "invoice", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(created));

        var service = CreateService(client);
        await service.SubscribeAsync(new SubscriberProfile(email, firstName: null, lastName: null), "eshop-pro", CancellationToken.None);

        await client.Received(1).CreateSubscriptionAsync("eshop-pro", customer.Id, "invoice", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptionsReturnsEmptyWhenShopperHasNoCustomerYet()
    {
        string email = "ghost@eshop.test";
        var client = Substitute.For<IMaxioBillingClient>();
        client.FindCustomerByReferenceAsync(email, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MaxioCustomer?>(null));

        var service = CreateService(client);
        var subscriptions = await service.ListSubscriptionsAsync(email, CancellationToken.None);

        Assert.Empty(subscriptions);
        await client.DidNotReceiveWithAnyArgs().ListCustomerSubscriptionsAsync(default, default);
    }

    private static MaxioSubscriptionService CreateService(IMaxioBillingClient client)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            Environment = "US",
            ProductFamilyHandle = ProductFamilyHandle
        });
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new MaxioSubscriptionService(client, options, cache, NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static MaxioSubscription NewSubscription(long id, string planHandle, string state)
    {
        return new MaxioSubscription
        {
            Id = id,
            State = state,
            Product = new MaxioProduct
            {
                Id = 700,
                Handle = planHandle,
                Name = "Pro Plan",
                PriceInCents = 29900,
                Interval = 1,
                IntervalUnit = "month"
            },
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
