using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private static readonly string UserId = Guid.NewGuid().ToString();

    private readonly IMaxioApiClient _api = Substitute.For<IMaxioApiClient>();
    private readonly MaxioBillingService _service;

    public MaxioBillingServiceTests()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle
        });

        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var normalizer = Substitute.For<ILookupNormalizer>();
        normalizer.NormalizeName(Arg.Any<string?>()).Returns(c => c.Arg<string?>());
        normalizer.NormalizeEmail(Arg.Any<string?>()).Returns(c => c.Arg<string?>());
        store.FindByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ApplicationUser { Id = UserId, UserName = "demouser@microsoft.com", Email = "demouser@microsoft.com" });

        var userManager = new UserManager<ApplicationUser>(
            store, Options.Create(new IdentityOptions()), null!, null!, null!, normalizer, null!, null!, NullLogger<UserManager<ApplicationUser>>.Instance);

        _service = new MaxioBillingService(_api, options, userManager, NullLogger<MaxioBillingService>.Instance);
    }

    private static MaxioProductDto Plan(string handle, long priceCents, string family = FamilyHandle) => new()
    {
        Id = 1,
        Handle = handle,
        Name = handle + " plan",
        PriceInCents = priceCents,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamily = new MaxioProductFamilyDto { Handle = family }
    };

    private static MaxioCustomerDto Customer(int id = 42) => new()
    {
        Id = id,
        Reference = UserId,
        Email = "demouser@microsoft.com"
    };

    private static MaxioSubscriptionDto Subscription(int id, string planHandle, string state) => new()
    {
        Id = id,
        State = state,
        Product = Plan(planHandle, 29900),
        ProductPriceInCents = 29900,
        CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddDays(20),
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task GetPlansAsync_ReturnsOnlyConfiguredFamilyPlans()
    {
        _api.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProductDto>
        {
            Plan("eshop-pro", 29900),
            Plan("other-family-product", 1000, "other-family"),
            new() { Handle = "archived-plan", ArchivedAt = DateTimeOffset.UtcNow, ProductFamily = new MaxioProductFamilyDto { Handle = FamilyHandle } }
        });

        var result = await _service.GetPlansAsync(CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        var plan = Assert.Single(result.Value);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceCents);
        Assert.Equal("1 month", plan.BillingInterval);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscriptionOnFirstEnrollment()
    {
        _api.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(Plan("eshop-pro", 29900));
        _api.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>()).Returns((MaxioCustomerDto?)null);
        _api.CreateCustomerAsync(Arg.Any<MaxioNewCustomer>(), Arg.Any<CancellationToken>()).Returns(Customer());
        _api.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscriptionDto>());
        _api.CreateSubscriptionAsync("eshop-pro", 42, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(101, "eshop-pro", "active"));

        var result = await _service.SubscribeAsync("demouser@microsoft.com", "eshop-pro", null, CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.False(result.Value.AlreadySubscribed);
        Assert.Equal(101, result.Value.Subscription.SubscriptionId);
        Assert.Equal("active", result.Value.Subscription.State);
        Assert.Equal(UserId, result.Value.BillingCustomerReference);
        await _api.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioNewCustomer>(c => c.Reference == UserId && c.Email == "demouser@microsoft.com"),
            Arg.Any<CancellationToken>());
        await _api.Received(1).CreateSubscriptionAsync("eshop-pro", 42, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ReusesExistingCustomer()
    {
        _api.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(Plan("eshop-pro", 29900));
        _api.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>()).Returns(Customer(77));
        _api.ListCustomerSubscriptionsAsync(77, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscriptionDto>());
        _api.CreateSubscriptionAsync("eshop-pro", 77, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(102, "eshop-pro", "active"));

        var result = await _service.SubscribeAsync("demouser@microsoft.com", "eshop-pro", null, CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(77, result.Value.BillingCustomerId);
        await _api.DidNotReceiveWithAnyArgs().CreateCustomerAsync(default!, default);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingLiveSubscription_InsteadOfCreatingADuplicate()
    {
        _api.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(Plan("eshop-pro", 29900));
        _api.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>()).Returns(Customer(42));
        _api.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionDto> { Subscription(101, "eshop-pro", "active") });

        var result = await _service.SubscribeAsync("demouser@microsoft.com", "eshop-pro", "fixed-key", CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.True(result.Value.AlreadySubscribed);
        Assert.Equal(101, result.Value.Subscription.SubscriptionId);
        await _api.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task SubscribeAsync_ForwardsIdempotencyKey_AsUniquenessToken()
    {
        _api.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(Plan("eshop-pro", 29900));
        _api.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>()).Returns(Customer(42));
        _api.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscriptionDto>());
        _api.CreateSubscriptionAsync("eshop-pro", 42, "fixed-key", Arg.Any<CancellationToken>())
            .Returns(Subscription(103, "eshop-pro", "active"));

        await _service.SubscribeAsync("demouser@microsoft.com", "eshop-pro", "fixed-key", CancellationToken.None);

        await _api.Received(1).CreateSubscriptionAsync("eshop-pro", 42, "fixed-key", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_AllowsResubscribeAfterCancellation()
    {
        _api.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(Plan("eshop-pro", 29900));
        _api.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>()).Returns(Customer(42));
        _api.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionDto> { Subscription(101, "eshop-pro", "canceled") });
        _api.CreateSubscriptionAsync("eshop-pro", 42, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(104, "eshop-pro", "active"));

        var result = await _service.SubscribeAsync("demouser@microsoft.com", "eshop-pro", null, CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.False(result.Value.AlreadySubscribed);
        Assert.Equal(104, result.Value.Subscription.SubscriptionId);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsPlanOutsideConfiguredFamily()
    {
        _api.GetProductByHandleAsync("foreign-plan", Arg.Any<CancellationToken>())
            .Returns(Plan("foreign-plan", 1000, "some-other-family"));

        var result = await _service.SubscribeAsync("demouser@microsoft.com", "foreign-plan", null, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        await _api.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task SubscribeAsync_WithMissingPlanHandle_IsInvalid()
    {
        var result = await _service.SubscribeAsync("demouser@microsoft.com", "", null, CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task GetSubscriptionsForUserAsync_WithNoBillingCustomerYet_ReturnsEmptyList()
    {
        _api.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>()).Returns((MaxioCustomerDto?)null);

        var result = await _service.GetSubscriptionsForUserAsync("demouser@microsoft.com", CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task GetSubscriptionsForUserAsync_ReturnsMappedSubscriptions()
    {
        var nextBilling = DateTimeOffset.UtcNow.AddDays(15);
        _api.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>()).Returns(Customer(42));
        _api.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionDto>
            {
                new()
                {
                    Id = 101,
                    State = "active",
                    Product = Plan("eshop-pro", 29900),
                    ProductPriceInCents = 29900,
                    CurrentPeriodEndsAt = nextBilling,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            });

        var result = await _service.GetSubscriptionsForUserAsync("demouser@microsoft.com", CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        var sub = Assert.Single(result.Value);
        Assert.Equal(101, sub.SubscriptionId);
        Assert.Equal("eshop-pro", sub.PlanHandle);
        Assert.Equal("active", sub.State);
        Assert.Equal(29900, sub.PriceCents);
        Assert.Equal(nextBilling, sub.NextBillingAt);
    }
}
