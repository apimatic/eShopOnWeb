using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "k",
        Subdomain = "cp-exp-8",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private readonly SubscriberInfo _subscriber = new()
    {
        Reference = "user-123",
        Email = "demo@example.com",
        FirstName = "Demo",
        LastName = "User"
    };

    private MaxioSubscriptionService CreateService() =>
        new(_client, _settings, Substitute.For<IAppLogger<MaxioSubscriptionService>>());

    [Fact]
    public async Task GetPlansAsync_MapsFiltersArchived_AndOrdersByPrice()
    {
        _client.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                Product("eshop-pro", 29900, "Pro Plan"),
                Product("basic-plan", 2900, "Basic Plan"),
                Product("legacy", 100, "Legacy", archivedAt: DateTimeOffset.UtcNow)
            });

        var plans = await CreateService().GetPlansAsync();

        Assert.Equal(2, plans.Count);                 // archived one filtered out
        Assert.Equal("basic-plan", plans[0].Handle);  // ordered by price ascending
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal(29m, plans[0].Price);
        Assert.Equal("eshop-subscribe", plans[0].ProductFamilyHandle);
    }

    [Fact]
    public async Task SubscribeAsync_UnknownPlan_ThrowsAndNeverTouchesCustomer()
    {
        _client.ListProductsForFamilyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Product("eshop-pro", 29900, "Pro Plan") });

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(_subscriber, "no-such-plan"));

        await _client.DidNotReceive().LookupCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionInput>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_WhenAlreadySubscribed_ReturnsExisting_AndDoesNotCreate()
    {
        _client.ListProductsForFamilyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Product("eshop-pro", 29900, "Pro Plan") });
        _client.LookupCustomerByReferenceAsync("user-123", Arg.Any<CancellationToken>())
            .Returns(Customer(42, "user-123"));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Sub(500, "active", "eshop-pro", 29900) });

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(500, result.Subscription.Id);
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomerInput>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionInput>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_NewCustomer_CreatesCustomerThenSubscription()
    {
        _client.ListProductsForFamilyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Product("eshop-pro", 29900, "Pro Plan") });
        _client.LookupCustomerByReferenceAsync("user-123", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerInput>(), Arg.Any<CancellationToken>())
            .Returns(Customer(42, "user-123"));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionInput>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Sub(777, "active", "eshop-pro", 29900));

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(777, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<CreateCustomerInput>(c => c.Reference == "user-123" && c.Email == "demo@example.com"),
            Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionInput>(s => s.ProductHandle == "eshop-pro" && s.PaymentCollectionMethod == "remittance"),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_DuplicateOnCreate_ResolvesToTheWinningSubscription()
    {
        _client.ListProductsForFamilyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Product("eshop-pro", 29900, "Pro Plan") });
        _client.LookupCustomerByReferenceAsync("user-123", Arg.Any<CancellationToken>())
            .Returns(Customer(42, "user-123"));
        // First check finds nothing; after the 409 the winning subscription becomes visible.
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(
                new List<MaxioSubscription>(),
                new List<MaxioSubscription> { Sub(888, "active", "eshop-pro", 29900) });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionInput>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException(HttpStatusCode.Conflict, Array.Empty<string>(), "duplicate"));

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(888, result.Subscription.Id);
    }

    [Fact]
    public async Task EnsureCustomer_WhenCreateLosesUniquenessRace_ReReadsExistingCustomer()
    {
        _client.ListProductsForFamilyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Product("eshop-pro", 29900, "Pro Plan") });
        // Lookup: null on the first attempt, then the customer another request created.
        _client.LookupCustomerByReferenceAsync("user-123", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, Customer(42, "user-123"));
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerInput>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException(HttpStatusCode.UnprocessableEntity,
                new[] { "Reference: must be unique - that value has been taken." }, "create customer"));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionInput>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Sub(999, "active", "eshop-pro", 29900));

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(999, result.Subscription.Id);
        // Subscribed against the customer that won the race (id 42).
        await _client.Received().ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptionsAsync_WhenNoCustomer_ReturnsEmpty()
    {
        _client.LookupCustomerByReferenceAsync("user-123", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var result = await CreateService().GetSubscriptionsAsync("user-123");

        Assert.Empty(result);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptionsAsync_MapsSubscriptions_NewestFirst()
    {
        _client.LookupCustomerByReferenceAsync("user-123", Arg.Any<CancellationToken>())
            .Returns(Customer(42, "user-123"));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                Sub(1, "active", "basic-plan", 2900, createdAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                Sub(2, "active", "eshop-pro", 29900, createdAt: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero))
            });

        var result = await CreateService().GetSubscriptionsAsync("user-123");

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Id);   // newest first
        Assert.Equal(1, result[1].Id);
        Assert.Equal(299m, result[0].Price);
    }

    private static MaxioProduct Product(string handle, long cents, string name, DateTimeOffset? archivedAt = null) => new()
    {
        Id = handle.GetHashCode() & 0x7fffffff,
        Handle = handle,
        Name = name,
        PriceInCents = cents,
        Interval = 1,
        IntervalUnit = "month",
        ArchivedAt = archivedAt,
        ProductFamily = new MaxioProductFamily { Handle = "eshop-subscribe" }
    };

    private static MaxioCustomer Customer(int id, string reference) => new()
    {
        Id = id,
        Reference = reference,
        Email = "demo@example.com",
        FirstName = "Demo",
        LastName = "User"
    };

    private static MaxioSubscription Sub(int id, string state, string handle, long cents, DateTimeOffset? createdAt = null) => new()
    {
        Id = id,
        State = state,
        ProductPriceInCents = cents,
        PaymentCollectionMethod = "remittance",
        CreatedAt = createdAt,
        NextAssessmentAt = createdAt?.AddMonths(1),
        Product = new MaxioProduct { Handle = handle, Name = handle, PriceInCents = cents }
    };
}
