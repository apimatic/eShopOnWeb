using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Subscriptions;

public class SubscriptionBillingServiceTests
{
    private readonly IMaxioBillingClient _maxio = Substitute.For<IMaxioBillingClient>();
    private readonly SubscriptionBillingService _service;
    private readonly ApplicationUser _user = new()
    {
        Id = "user-123",
        UserName = "shopper@example.com",
        Email = "shopper@example.com"
    };

    public SubscriptionBillingServiceTests()
    {
        _service = new SubscriptionBillingService(_maxio, Options.Create(new MaxioOptions
        {
            ApiKey = "not-a-secret",
            Subdomain = "test",
            ProductFamilyHandle = "family"
        }));
    }

    [Fact]
    public async Task SubscribeReturnsExistingSubscriptionWithoutCreatingAnything()
    {
        var existing = Subscription(42);
        _maxio.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _service.SubscribeAsync(_user, "pro", CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(42, result.Subscription.Id);
        await _maxio.DidNotReceive().ListProductsAsync(Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateMaxioCustomer>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionUsingStableReferences()
    {
        _maxio.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _maxio.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new[] { Product("pro") });
        _maxio.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<CreateMaxioCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 7, Reference = "eshop-user:user-123" });
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(99));

        var result = await _service.SubscribeAsync(_user, "pro", CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<CreateMaxioCustomer>(request =>
                request.Reference == "eshop-user:user-123" &&
                request.Email == "shopper@example.com" &&
                IsGuid(request.UniquenessToken)),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateMaxioSubscription>(request =>
                request.CustomerId == 7 &&
                request.ProductHandle == "pro" &&
                request.Reference.StartsWith("eshop-sub:user-123:", StringComparison.Ordinal) &&
                IsGuid(request.UniquenessToken)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConcurrentSubscribeRequestsCreateOnlyOneSubscription()
    {
        var created = Subscription(101);
        var lookupCount = 0;
        _maxio.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref lookupCount) == 1 ? null : created);
        _maxio.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new[] { Product("pro") });
        _maxio.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 7 });
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscription>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(50);
                return created;
            });

        var results = await Task.WhenAll(
            _service.SubscribeAsync(_user, "pro", CancellationToken.None),
            _service.SubscribeAsync(_user, "pro", CancellationToken.None));

        Assert.Single(results.Where(result => result.Created));
        Assert.All(results, result => Assert.Equal(101, result.Subscription.Id));
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Any<CreateMaxioSubscription>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptionsUsesCustomerReferenceAndFiltersConfiguredFamily()
    {
        _maxio.FindCustomerAsync("eshop-user:user-123", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 7 });
        _maxio.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(1, "family"), Subscription(2, "other-family") });

        var result = await _service.ListSubscriptionsAsync(_user, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
    }

    [Fact]
    public async Task ListPlansOmitsArchivedProductsAndOrdersByPrice()
    {
        _maxio.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Product("archived", 10, DateTimeOffset.UtcNow),
            Product("pro", 29900),
            Product("basic", 2900)
        });

        var result = await _service.ListPlansAsync(CancellationToken.None);

        Assert.Collection(result,
            plan => Assert.Equal("basic", plan.Handle),
            plan => Assert.Equal("pro", plan.Handle));
    }

    private static MaxioProduct Product(string handle, long price = 29900, DateTimeOffset? archivedAt = null) => new()
    {
        Id = 1,
        Handle = handle,
        Name = handle,
        PriceInCents = price,
        Interval = 1,
        IntervalUnit = "month",
        ArchivedAt = archivedAt,
        ProductFamilyHandle = "family"
    };

    private static MaxioSubscription Subscription(int id, string family = "family") => new()
    {
        Id = id,
        State = "active",
        ProductPriceInCents = 29900,
        NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
        Product = new MaxioProduct
        {
            Handle = "pro",
            Name = "Pro",
            Interval = 1,
            IntervalUnit = "month",
            ProductFamilyHandle = family
        }
    };

    private static bool IsGuid(string value) => Guid.TryParse(value, out _);
}
