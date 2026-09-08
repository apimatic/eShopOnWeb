using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Subscriptions;

public class SubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string Username = "demouser@microsoft.com";

    private readonly IMaxioClient _maxioClient = Substitute.For<IMaxioClient>();
    private readonly UserManager<ApplicationUser> _userManager = new(
        Substitute.For<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, NullLogger<UserManager<ApplicationUser>>.Instance);
    private readonly SubscriptionService _service;

    public SubscriptionServiceTests()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle
        });
        _service = new SubscriptionService(_maxioClient, _userManager, new MemoryCache(new MemoryCacheOptions()), options, NullLogger<SubscriptionService>.Instance);
    }

    [Fact]
    public async Task ReturnsExistingActiveSubscriptionInsteadOfCreatingDuplicate()
    {
        _maxioClient.ListProductsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { Product("eshop-pro", 29900) });
        _maxioClient.FindCustomerByReferenceAsync(Username, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = Username });
        _maxioClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(94241254, "active", "eshop-pro") });

        var result = await _service.SubscribeAsync(Username, "eshop-pro");

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Created);
        Assert.Equal(94241254, result.Value.Subscription.SubscriptionId);
        await _maxioClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>());
        await _maxioClient.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateMaxioCustomerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatesSubscriptionWhenCustomerAndProductAreNew()
    {
        _maxioClient.ListProductsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { Product("eshop-pro", 29900) });
        _maxioClient.FindCustomerByReferenceAsync(Username, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = Username });
        _maxioClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>());
        _maxioClient.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(94241254, "active", "eshop-pro"));

        var result = await _service.SubscribeAsync(Username, "eshop-pro");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Created);
        await _maxioClient.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateMaxioSubscriptionRequest>(r => r.ProductHandle == "eshop-pro" && r.CustomerId == 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatesMaxioCustomerOnFirstSubscribe()
    {
        _maxioClient.FindCustomerByReferenceAsync(Username, Arg.Any<CancellationToken>())
            .Returns(_ => (MaxioCustomer?)null);
        _userManager.FindByNameAsync(Username).Returns(new ApplicationUser { UserName = Username, Email = Username });
        _maxioClient.CreateCustomerAsync(Arg.Any<CreateMaxioCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 7, Reference = Username });
        _maxioClient.ListProductsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { Product("eshop-pro", 29900) });
        _maxioClient.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>());
        _maxioClient.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(94241254, "active", "eshop-pro", customerId: 7));

        var result = await _service.SubscribeAsync(Username, "eshop-pro");

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value.Subscription.CustomerId);
        await _maxioClient.Received(1).CreateCustomerAsync(
            Arg.Is<CreateMaxioCustomerRequest>(r => r.Reference == Username && r.Email == Username),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReusesCustomerAfterCreationRace()
    {
        _maxioClient.FindCustomerByReferenceAsync(Username, Arg.Any<CancellationToken>())
            .Returns(_ => null,
                     _ => new MaxioCustomer { Id = 99, Reference = Username });
        _userManager.FindByNameAsync(Username).Returns(new ApplicationUser { UserName = Username, Email = Username });
        _maxioClient.CreateCustomerAsync(Arg.Any<CreateMaxioCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns((Func<NSubstitute.Core.CallInfo, MaxioCustomer>)(_ => throw new MaxioApiException(422, new[] { "Reference has already been taken." }, "422")));
        _maxioClient.ListProductsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { Product("eshop-pro", 29900) });
        _maxioClient.ListCustomerSubscriptionsAsync(99, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>());
        _maxioClient.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(94241254, "active", "eshop-pro", customerId: 99));

        var result = await _service.SubscribeAsync(Username, "eshop-pro");

        Assert.True(result.IsSuccess);
        Assert.Equal(99, result.Value.Subscription.CustomerId);
    }

    [Fact]
    public async Task UnknownPlanReturnsNotFound()
    {
        _maxioClient.ListProductsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { Product("eshop-pro", 29900) });

        var result = await _service.SubscribeAsync(Username, "does-not-exist");

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetMySubscriptionsReturnsEmptyListWhenNoMaxioCustomerExists()
    {
        _maxioClient.FindCustomerByReferenceAsync(Username, Arg.Any<CancellationToken>())
            .Returns(_ => (MaxioCustomer?)null);

        var result = await _service.GetMySubscriptionsAsync(Username);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task GetMySubscriptionsMapsPlanPriceStateAndNextBillingDate()
    {
        var nextBilling = DateTimeOffset.Parse("2026-10-08T14:00:00-04:00");
        _maxioClient.FindCustomerByReferenceAsync(Username, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = Username });
        _maxioClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                Subscription(94241254, "active", "eshop-pro", 29900, nextBilling),
                Subscription(94241257, "canceled", "basic-plan", 2900, nextBilling)
            });

        var result = await _service.GetMySubscriptionsAsync(Username);

        Assert.Equal(2, result.Value.Count);
        var pro = result.Value.Single(s => s.PlanHandle == "eshop-pro");
        Assert.Equal("active", pro.State);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299m, pro.Price);
        Assert.Equal(nextBilling, pro.NextBillingDate);
        Assert.Equal(42, pro.CustomerId);
    }

    [Fact]
    public async Task GetPlansReturnsOnlyProductsFromConfiguredFamily()
    {
        _maxioClient.ListProductsAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                Product("eshop-pro", 29900),
                Product("other", 100, familyHandle: "other-family")
            });

        var result = await _service.GetPlansAsync();

        var plan = Assert.Single(result.Value);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299m, plan.Price);
        Assert.Equal(FamilyHandle, plan.ProductFamilyHandle);
    }

    private static MaxioProduct Product(string handle, long priceInCents, string familyHandle = FamilyHandle) =>
        new()
        {
            Id = 1,
            Handle = handle,
            Name = handle,
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month",
            ProductFamily = new MaxioProductFamily { Id = 3048505, Handle = familyHandle }
        };

    private static MaxioSubscription Subscription(int id, string state, string productHandle, long priceInCents = 29900, DateTimeOffset? nextBilling = null, int customerId = 42) =>
        new()
        {
            Id = id,
            State = state,
            ProductPriceInCents = priceInCents,
            CurrentPeriodEndsAt = nextBilling ?? DateTimeOffset.UtcNow.AddDays(30),
            ActivatedAt = DateTimeOffset.UtcNow,
            PaymentCollectionMethod = "remittance",
            Customer = new MaxioCustomer { Id = customerId, Reference = Username },
            Product = new MaxioProduct { Handle = productHandle, Name = productHandle, PriceInCents = priceInCents }
        };
}

