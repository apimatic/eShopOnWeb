using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string UserId = "user-1";
    private const string PlanHandle = "eshop-pro";
    private const int FamilyId = 123;

    private readonly IMaxioClient _maxioClient = Substitute.For<IMaxioClient>();
    private readonly MaxioSubscriptionService _service;

    public MaxioSubscriptionServiceTests()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        });
        _service = new MaxioSubscriptionService(
            _maxioClient,
            new MemoryCache(new MemoryCacheOptions()),
            options,
            NullLogger<MaxioSubscriptionService>.Instance);
    }

    private void SetupFamilyWithPlan(string planHandle = PlanHandle)
    {
        _maxioClient.FindProductFamilyByHandleAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new MaxioProductFamily { Id = FamilyId, Handle = "eshop-subscribe" });
        _maxioClient.ListProductsInFamilyAsync(FamilyId, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Id = 1, Handle = planHandle, Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
            });
    }

    private void SetupCustomer(int customerId = 77)
    {
        _maxioClient.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = customerId, Reference = UserId, Email = "user1@test.com" });
    }

    [Fact]
    public async Task SubscribeAsyncCreatesSubscriptionWhenNoneExists()
    {
        SetupFamilyWithPlan();
        SetupCustomer();
        _maxioClient.ListCustomerSubscriptionsAsync(77, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _maxioClient.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription
            {
                Id = 999,
                State = "active",
                ProductPriceInCents = 29900,
                CurrentPeriodEndsAt = new DateTime(2026, 10, 8),
                Product = new MaxioSubscriptionProduct { Id = 1, Handle = PlanHandle, Name = "Pro Plan", PriceInCents = 29900 }
            });

        var result = await _service.SubscribeAsync(UserId, "user1@test.com", "user1@test.com", PlanHandle, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(999, result.Value.SubscriptionId);
        Assert.Equal("active", result.Value.State);
        Assert.Equal(299m, result.Value.Price);
        Assert.False(result.Value.AlreadySubscribed);
        await _maxioClient.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionRequest>(r => r.CustomerId == 77 && r.ProductHandle == PlanHandle),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsyncIsIdempotentWhenUserAlreadyHoldsPlan()
    {
        SetupFamilyWithPlan();
        SetupCustomer();
        _maxioClient.ListCustomerSubscriptionsAsync(77, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new()
                {
                    Id = 555,
                    State = "active",
                    Product = new MaxioSubscriptionProduct { Id = 1, Handle = PlanHandle, Name = "Pro Plan", PriceInCents = 29900 }
                }
            });

        var result = await _service.SubscribeAsync(UserId, "user1@test.com", "user1@test.com", PlanHandle, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(555, result.Value.SubscriptionId);
        Assert.True(result.Value.AlreadySubscribed);
        // The create API must never be called: no duplicate subscription.
        await _maxioClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsyncCreatesNewSubscriptionWhenPreviousOneWasCanceled()
    {
        SetupFamilyWithPlan();
        SetupCustomer();
        _maxioClient.ListCustomerSubscriptionsAsync(77, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new()
                {
                    Id = 556,
                    State = "canceled",
                    Product = new MaxioSubscriptionProduct { Id = 1, Handle = PlanHandle, Name = "Pro Plan", PriceInCents = 29900 }
                }
            });
        _maxioClient.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 1000, State = "active", Product = new MaxioSubscriptionProduct { Handle = PlanHandle, Name = "Pro Plan" } });

        var result = await _service.SubscribeAsync(UserId, "user1@test.com", "user1@test.com", PlanHandle, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1000, result.Value.SubscriptionId);
        await _maxioClient.Received(1).CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsyncReturnsNotFoundForUnknownPlan()
    {
        SetupFamilyWithPlan();

        var result = await _service.SubscribeAsync(UserId, "user1@test.com", "user1@test.com", "no-such-plan", CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        _maxioClient.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default);
        _maxioClient.DidNotReceiveWithAnyArgs().CreateCustomerAsync(default!, default);
    }

    [Fact]
    public async Task SubscribeAsyncCreatesCustomerOnlyWhenLookupMisses()
    {
        SetupFamilyWithPlan();
        _maxioClient.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _maxioClient.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 88, Reference = UserId });
        _maxioClient.ListCustomerSubscriptionsAsync(88, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _maxioClient.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 1001, State = "active", Product = new MaxioSubscriptionProduct { Handle = PlanHandle, Name = "Pro Plan" } });

        var result = await _service.SubscribeAsync(UserId, "user1@test.com", "user1@test.com", PlanHandle, CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _maxioClient.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomerRequest>(r => r.Reference == UserId && r.Email == "user1@test.com"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsyncRejectsEmptyPlanHandle()
    {
        var result = await _service.SubscribeAsync(UserId, "user1@test.com", "user1@test.com", "", CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task GetSubscriptionsForUserAsyncReturnsEmptyWhenNoMaxioCustomer()
    {
        _maxioClient.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var result = await _service.GetSubscriptionsForUserAsync(UserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task GetPlansAsyncFiltersArchivedProducts()
    {
        _maxioClient.FindProductFamilyByHandleAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new MaxioProductFamily { Id = FamilyId, Handle = "eshop-subscribe" });
        _maxioClient.ListProductsInFamilyAsync(FamilyId, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Id = 1, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 },
                new() { Id = 2, Handle = "old-plan", Name = "Old Plan", PriceInCents = 100, ArchivedAt = DateTime.UtcNow }
            });

        var result = await _service.GetPlansAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var plan = Assert.Single(result.Value);
        Assert.Equal("eshop-pro", plan.Handle);
    }
}
