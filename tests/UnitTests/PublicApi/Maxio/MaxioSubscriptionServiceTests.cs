using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Maxio;

public class MaxioSubscriptionServiceTests
{
    private static readonly MaxioOptions Options = new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private static MaxioProduct Product(string handle, long priceInCents, DateTime? archivedAt = null) =>
        new()
        {
            Handle = handle,
            Name = $"{handle} name",
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month",
            ArchivedAt = archivedAt
        };

    private static MaxioCustomer Customer(int id = 1, string reference = "user1@test.com") =>
        new() { Id = id, Reference = reference, Email = reference, FirstName = "Test", LastName = "User" };

    private static MaxioSubscription Subscription(
        int id, string handle, string state, int customerId = 1) =>
        new()
        {
            Id = id,
            State = state,
            Product = new MaxioProduct { Handle = handle, Name = $"{handle} name", PriceInCents = 9900, Interval = 1, IntervalUnit = "month" },
            Customer = new MaxioCustomer { Id = customerId },
            CurrentPeriodEndsAt = new DateTime(2030, 1, 1),
            CreatedAt = new DateTime(2029, 12, 1)
        };

    private class StubLogger : IAppLogger<MaxioSubscriptionService>
    {
        public void LogInformation(string message, params object[] args) { }
        public void LogWarning(string message, params object[] args) { }
    }

    private static (MaxioSubscriptionService Service, IMaxioApiClient Client) BuildService(
        MaxioCustomer? customerByReference = null,
        IReadOnlyList<MaxioSubscription>? customerSubscriptions = null,
        IReadOnlyList<MaxioProduct>? products = null)
    {
        var client = Substitute.For<IMaxioApiClient>();
        client.ListProductsForFamilyAsync(Options.ProductFamilyHandle!, Arg.Any<CancellationToken>())
            .Returns(products ?? new List<MaxioProduct> { Product("eshop-pro", 29900), Product("basic-plan", 2900) });
        client.GetCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(customerByReference);
        client.ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(customerSubscriptions ?? new List<MaxioSubscription>());

        var service = new MaxioSubscriptionService(
            client, Microsoft.Extensions.Options.Options.Create(Options), new StubLogger());
        return (service, client);
    }

    [Fact]
    public async Task SubscribeAsyncReturnsExistingLiveSubscriptionInsteadOfCreatingDuplicate()
    {
        var existing = Subscription(100, "eshop-pro", "active");
        var (service, client) = BuildService(
            customerByReference: Customer(1),
            customerSubscriptions: new List<MaxioSubscription> { existing });

        var result = await service.SubscribeAsync("user1@test.com", "eshop-pro");

        Assert.Equal(100, result.MaxioSubscriptionId);
        Assert.Equal("active", result.State);
        await client.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default, default);
    }

    [Fact]
    public async Task SubscribeAsyncCreatesNewSubscriptionWhenOnlyCanceledSubscriptionExists()
    {
        var canceled = Subscription(100, "eshop-pro", "canceled");
        var created = Subscription(200, "eshop-pro", "active");
        var (service, client) = BuildService(
            customerByReference: Customer(1),
            customerSubscriptions: new List<MaxioSubscription> { canceled });
        client.CreateSubscriptionAsync("eshop-pro", 1, Arg.Any<CancellationToken>())
            .Returns(created);

        var result = await service.SubscribeAsync("user1@test.com", "eshop-pro");

        Assert.Equal(200, result.MaxioSubscriptionId);
        await client.Received(1).CreateSubscriptionAsync("eshop-pro", 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsyncAllowsSameCustomerToHoldDifferentPlans()
    {
        var otherPlan = Subscription(100, "basic-plan", "active");
        var created = Subscription(200, "eshop-pro", "active");
        var (service, client) = BuildService(
            customerByReference: Customer(1),
            customerSubscriptions: new List<MaxioSubscription> { otherPlan });
        client.CreateSubscriptionAsync("eshop-pro", 1, Arg.Any<CancellationToken>())
            .Returns(created);

        var result = await service.SubscribeAsync("user1@test.com", "eshop-pro");

        Assert.Equal(200, result.MaxioSubscriptionId);
    }

    [Fact]
    public async Task SubscribeAsyncThrowsNotFoundForUnknownPlanHandle()
    {
        var (service, _) = BuildService(customerByReference: Customer(1));

        var ex = await Assert.ThrowsAsync<MaxioApiException>(
            () => service.SubscribeAsync("user1@test.com", "does-not-exist"));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task SubscribeAsyncReusesExistingCustomerWithoutCreating()
    {
        var (service, client) = BuildService(
            customerByReference: Customer(42, "user1@test.com"),
            customerSubscriptions: new List<MaxioSubscription>());
        client.CreateSubscriptionAsync("eshop-pro", 42, Arg.Any<CancellationToken>())
            .Returns(Subscription(200, "eshop-pro", "active"));

        await service.SubscribeAsync("user1@test.com", "eshop-pro");

        await client.DidNotReceiveWithAnyArgs().CreateCustomerAsync(default!, default);
        await client.Received(1).CreateSubscriptionAsync("eshop-pro", 42, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureCustomerRecoversWhenConcurrentCreateLosesReferenceRace()
    {
        var client = Substitute.For<IMaxioApiClient>();
        client.ListProductsForFamilyAsync(Options.ProductFamilyHandle!, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Product("eshop-pro", 29900) });
        var lookupCount = 0;
        client.GetCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => ++lookupCount == 1
                ? null
                : Customer(77, "user1@test.com"));
        client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerBody>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MaxioCustomer>(new MaxioApiException("duplicate reference", 422)));
        client.CreateSubscriptionAsync("eshop-pro", 77, Arg.Any<CancellationToken>())
            .Returns(Subscription(200, "eshop-pro", "active", customerId: 77));

        var service = new MaxioSubscriptionService(
            client, Microsoft.Extensions.Options.Options.Create(Options), new StubLogger());

        var result = await service.SubscribeAsync("user1@test.com", "eshop-pro");

        // If the re-lookup had failed or returned the wrong customer, the
        // subscription create would have been attempted against another id.
        Assert.Equal(200, result.MaxioSubscriptionId);
        await client.Received(1).CreateCustomerAsync(Arg.Any<MaxioCreateCustomerBody>(), Arg.Any<CancellationToken>());
        await client.DidNotReceive().CreateSubscriptionAsync("eshop-pro", Arg.Is<int>(id => id != 77), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListPlansAsyncExcludesArchivedProducts()
    {
        var (service, _) = BuildService(products: new List<MaxioProduct>
        {
            Product("eshop-pro", 29900),
            Product("basic-plan", 2900, archivedAt: DateTime.UtcNow)
        });

        var plans = await service.ListPlansAsync();

        var handles = plans.Select(p => p.Handle).ToList();
        Assert.Contains("eshop-pro", handles);
        Assert.DoesNotContain("basic-plan", handles);
    }

    [Fact]
    public async Task GetMySubscriptionsReturnsCustomerSubscriptions()
    {
        var subs = new List<MaxioSubscription>
        {
            Subscription(100, "eshop-pro", "active"),
            Subscription(101, "basic-plan", "canceled")
        };
        var (service, _) = BuildService(
            customerByReference: Customer(1),
            customerSubscriptions: subs);

        var result = await service.GetMySubscriptionsAsync("user1@test.com");

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.MaxioSubscriptionId == 100 && s.PlanHandle == "eshop-pro");
        Assert.Contains(result, s => s.MaxioSubscriptionId == 101 && s.PlanHandle == "basic-plan");
    }
}