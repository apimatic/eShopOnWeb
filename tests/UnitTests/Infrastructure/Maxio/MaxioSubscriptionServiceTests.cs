using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "test-family";
    private const string UserId = "11111111-1111-1111-1111-111111111111";

    private readonly IMaxioClient _client = Substitute.For<IMaxioClient>();
    private readonly MaxioOptions _options = new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = FamilyHandle
    };

    private readonly MaxioSubscriptionService _service;
    private readonly SubscriptionUserData _user = new()
    {
        UserId = UserId,
        UserName = "demouser@microsoft.com",
        Email = "demouser@microsoft.com"
    };

    public MaxioSubscriptionServiceTests()
    {
        _service = new MaxioSubscriptionService(
            _client,
            Options.Create(_options),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MaxioSubscriptionService>.Instance);

        _client.ListFamilyProductsAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Id = 1, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
                new() { Id = 2, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" }
            });
    }

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscription()
    {
        _client.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 50, Reference = UserId });
        _client.ListCustomerSubscriptionsAsync(50, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 900, State = "active", Reference = $"{UserId}:eshop-pro" });

        var result = await _service.SubscribeAsync(_user, "eshop-pro");

        Assert.Equal(900, result.Id);
        Assert.Equal("active", result.State);

        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomer>(c =>
                c.Reference == UserId &&
                c.Email == "demouser@microsoft.com" &&
                !string.IsNullOrWhiteSpace(c.FirstName) &&
                !string.IsNullOrWhiteSpace(c.LastName)),
            Arg.Any<CancellationToken>());

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(s =>
                s.ProductHandle == "eshop-pro" &&
                s.CustomerId == 50 &&
                s.Reference == $"{UserId}:eshop-pro"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeIsIdempotentWhenSubscriptionAlreadyExists()
    {
        _client.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 50, Reference = UserId });
        _client.ListCustomerSubscriptionsAsync(50, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new() { Id = 900, State = "active", Reference = $"{UserId}:eshop-pro" }
            });

        var result = await _service.SubscribeAsync(_user, "eshop-pro");

        Assert.Equal(900, result.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeIsIdempotentWhenCustomerCreationRaces()
    {
        var racedCustomer = new MaxioCustomer { Id = 50, Reference = UserId };
        _client.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, racedCustomer);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(422, new[] { "Customer reference has already been taken." }));
        _client.ListCustomerSubscriptionsAsync(50, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 900, State = "active", Reference = $"{UserId}:eshop-pro" });

        var result = await _service.SubscribeAsync(_user, "eshop-pro");

        Assert.NotNull(result);
        await _client.Received(1).CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeResolvesConcurrencyRaceOnSubscriptionCreation()
    {
        _client.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 50, Reference = UserId });
        _client.ListCustomerSubscriptionsAsync(50, Arg.Any<CancellationToken>())
            .Returns(
                new List<MaxioSubscription>(),
                new List<MaxioSubscription> { new() { Id = 901, State = "active", Reference = $"{UserId}:eshop-pro" } });
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(422, new[] { "Reference has already been taken." }));

        var result = await _service.SubscribeAsync(_user, "eshop-pro");

        Assert.Equal(901, result.Id);
    }

    [Fact]
    public async Task SubscribeWithUnknownPlanThrows()
    {
        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => _service.SubscribeAsync(_user, "does-not-exist"));

        Assert.Equal("does-not-exist", exception.PlanHandle);
    }

    [Fact]
    public async Task ListPlansReturnsPlansFromFamily()
    {
        var plans = await _service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, p => p.Handle == "eshop-pro" && p.PriceInCents == 29900);
        Assert.Contains(plans, p => p.Handle == "basic-plan" && p.PriceInCents == 2900);
    }

    [Fact]
    public async Task ListPlansExcludesArchivedProducts()
    {
        _client.ListFamilyProductsAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Id = 1, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 },
                new() { Id = 3, Handle = "old-plan", Name = "Old Plan", PriceInCents = 100, ArchivedAt = DateTimeOffset.UtcNow }
            });

        var plans = await _service.ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans.Single().Handle);
    }

    [Fact]
    public async Task ListUserSubscriptionsReturnsEmptyWhenNoMaxioCustomerExists()
    {
        _client.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var result = await _service.ListUserSubscriptionsAsync(_user);

        Assert.Empty(result);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListUserSubscriptionsMapsFields()
    {
        _client.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 50, Reference = UserId });
        _client.ListCustomerSubscriptionsAsync(50, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new()
                {
                    Id = 900,
                    State = "active",
                    Currency = "USD",
                    BalanceInCents = 500,
                    ProductPriceInCents = 29900,
                    CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddDays(20),
                    Product = new MaxioProduct { Id = 1, Handle = "eshop-pro", Name = "Pro Plan", ProductFamily = new MaxioProductFamily { Handle = FamilyHandle } }
                }
            });

        var result = await _service.ListUserSubscriptionsAsync(_user);

        var subscription = result.Single();
        Assert.Equal(900, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal("USD", subscription.Currency);
        Assert.Equal(500, subscription.BalanceInCents);
        Assert.NotNull(subscription.NextBillingDateUtc);
    }
}