using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";

    private static MaxioOptions BuildOptions()
    {
        return new MaxioOptions
        {
            ApiKey = "test-api-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle
        };
    }

    private static MaxioSubscriptionService BuildService(IMaxioClient client, MaxioOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(client);
        var serviceProvider = services.BuildServiceProvider();
        return new MaxioSubscriptionService(serviceProvider, Options.Create(options ?? BuildOptions()));
    }

    private static SubscriptionSubscriber BuildSubscriber()
    {
        return new SubscriptionSubscriber("user-ref-1", "shopper@example.com");
    }

    [Fact]
    public async Task ListsOnlyActivePlansFromConfiguredFamily()
    {
        var client = Substitute.For<IMaxioClient>();
        client.ListProductFamiliesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProductFamilyEnvelope>
            {
                new() { ProductFamily = new MaxioProductFamily { Id = 42, Handle = FamilyHandle } },
                new() { ProductFamily = new MaxioProductFamily { Id = 7, Handle = "other-family" } }
            });
        client.ListProductsForFamilyAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProductEnvelope>
            {
                new() { Product = new MaxioProduct { Id = 1, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" } },
                new() { Product = new MaxioProduct { Id = 2, Handle = "retired-plan", Name = "Retired", PriceInCents = 100, ArchivedAt = DateTimeOffset.UtcNow } }
            });

        var service = BuildService(client);

        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeCreatesCustomerOnceAndIsIdempotentForRepeatedCalls()
    {
        var client = Substitute.For<IMaxioClient>();
        client.FindCustomerByReferenceAsync("user-ref-1", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomerEnvelope?)null);
        client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerEnvelope { Customer = new MaxioCustomer { Id = 55, Reference = "user-ref-1" } });
        client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscriptionEnvelope?)null);
        client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscriptionEnvelope
            {
                Subscription = new MaxioSubscription
                {
                    Id = 1,
                    State = "active",
                    Reference = "user-ref-1-eshop-pro",
                    ProductPriceInCents = 29900,
                    Currency = "USD",
                    Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
                }
            });

        var service = BuildService(client);

        var first = await service.SubscribeAsync(BuildSubscriber(), "eshop-pro");
        Assert.False(first.AlreadySubscribed);
        Assert.Equal(1, first.Subscription.Id);
        Assert.Equal("active", first.Subscription.State);

        client.FindCustomerByReferenceAsync("user-ref-1", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerEnvelope { Customer = new MaxioCustomer { Id = 55, Reference = "user-ref-1" } });
        client.FindSubscriptionByReferenceAsync("user-ref-1-eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscriptionEnvelope
            {
                Subscription = new MaxioSubscription
                {
                    Id = 1,
                    State = "active",
                    Reference = "user-ref-1-eshop-pro",
                    ProductPriceInCents = 29900,
                    Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
                }
            });

        var second = await service.SubscribeAsync(BuildSubscriber(), "eshop-pro");

        Assert.True(second.AlreadySubscribed);
        Assert.Equal(1, second.Subscription.Id);
        await client.Received(1).CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
        await client.Received(1).CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
        await client.Received(2).FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeTreatsConcurrentDuplicateAsAlreadySubscribed()
    {
        var client = Substitute.For<IMaxioClient>();
        client.FindCustomerByReferenceAsync("user-ref-1", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomerEnvelope?)null);
        client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerEnvelope { Customer = new MaxioCustomer { Id = 55, Reference = "user-ref-1" } });

        var findCalls = 0;
        client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                findCalls++;
                return findCalls > 1
                    ? new MaxioSubscriptionEnvelope
                    {
                        Subscription = new MaxioSubscription
                        {
                            Id = 77,
                            State = "active",
                            Reference = "user-ref-1-eshop-pro",
                            ProductPriceInCents = 29900,
                            Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
                        }
                    }
                    : null;
            });
        client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MaxioSubscriptionEnvelope>(
                new MaxioApiException(System.Net.HttpStatusCode.UnprocessableEntity,
                    new[] { "Reference: must be unique - that value has been taken." })));

        var service = BuildService(client);

        var result = await service.SubscribeAsync(BuildSubscriber(), "eshop-pro");

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(77, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeThrowsWhenPlanDoesNotExist()
    {
        var client = Substitute.For<IMaxioClient>();
        client.FindCustomerByReferenceAsync("user-ref-1", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomerEnvelope?)null);
        client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerEnvelope { Customer = new MaxioCustomer { Id = 55, Reference = "user-ref-1" } });
        client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscriptionEnvelope?)null);
        client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MaxioSubscriptionEnvelope>(
                new MaxioApiException(System.Net.HttpStatusCode.UnprocessableEntity,
                    new[] { "Product with API Handle 'nope' does not exist for this site." })));

        var service = BuildService(client);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => service.SubscribeAsync(BuildSubscriber(), "nope"));
    }

    [Fact]
    public async Task ListSubscriptionsReturnsEmptyWhenUserHasNoCustomer()
    {
        var client = Substitute.For<IMaxioClient>();
        client.FindCustomerByReferenceAsync("user-ref-1", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomerEnvelope?)null);

        var service = BuildService(client);

        var subscriptions = await service.ListSubscriptionsAsync(BuildSubscriber());

        Assert.Empty(subscriptions);
        await client.DidNotReceive().ListSubscriptionsForCustomerAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
