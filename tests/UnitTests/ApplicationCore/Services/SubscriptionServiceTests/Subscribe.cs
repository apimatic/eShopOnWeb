using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Subscribe
{
    private const string UserId = "user-123";
    private const string Email = "demouser@microsoft.com";
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly ISubscriptionBillingSettings _settings = Substitute.For<ISubscriptionBillingSettings>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    public Subscribe()
    {
        _settings.ProductFamilyHandle.Returns("eshop-subscribe");
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", default).Returns(new List<SubscriptionPlan>
        {
            new() { Id = 1, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Price = 299m, Interval = 1, IntervalUnit = "month" },
            new() { Id = 2, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Price = 29m, Interval = 1, IntervalUnit = "month" }
        });
    }

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        _maxio.FindCustomerByReferenceAsync(UserId, default).Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<CreateBillingCustomer>(), default)
            .Returns(new BillingCustomer { Id = 42, Reference = UserId, Email = Email });
        _maxio.ListCustomerSubscriptionsAsync(42, default).Returns(new List<ShopperSubscription>());
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), default).Returns((ShopperSubscription?)null);
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), default)
            .Returns(new ShopperSubscription
            {
                Id = 99,
                State = "active",
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan",
                PriceInCents = 29900,
                Price = 299m
            });

        var service = CreateService();
        var result = await service.SubscribeAsync(new SubscribeShopperRequest
        {
            UserId = UserId,
            Email = Email,
            UserName = Email,
            ProductHandle = "eshop-pro"
        });

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.ProductHandle);
        await _maxio.Received(1).CreateCustomerAsync(Arg.Is<CreateBillingCustomer>(c => c.Reference == UserId && c.Email == Email), default);
        await _maxio.Received(1).CreateSubscriptionAsync(Arg.Is<CreateBillingSubscription>(s =>
            s.CustomerId == 42 && s.ProductHandle == "eshop-pro" && s.Reference == $"{UserId}:eshop-pro"), default);
    }

    [Fact]
    public async Task DoesNotCreateASecondCustomerOrSubscriptionOnRetry()
    {
        var customer = new BillingCustomer { Id = 42, Reference = UserId, Email = Email };
        var existing = new ShopperSubscription
        {
            Id = 99,
            State = "active",
            ProductHandle = "eshop-pro",
            ProductName = "Pro Plan",
            PriceInCents = 29900,
            Price = 299m
        };

        _maxio.FindCustomerByReferenceAsync(UserId, default).Returns(customer);
        _maxio.ListCustomerSubscriptionsAsync(42, default).Returns(new List<ShopperSubscription> { existing });

        var service = CreateService();
        var result = await service.SubscribeAsync(new SubscribeShopperRequest
        {
            UserId = UserId,
            Email = Email,
            UserName = Email,
            ProductHandle = "eshop-pro"
        });

        Assert.Equal(99, result.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateBillingCustomer>(), default);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), default);
    }

    [Fact]
    public async Task RecoversWhenCreateCustomerLosesARace()
    {
        var recovered = new BillingCustomer { Id = 7, Reference = UserId, Email = Email };
        _maxio.FindCustomerByReferenceAsync(UserId, default)
            .Returns((BillingCustomer?)null, recovered);
        _maxio.CreateCustomerAsync(Arg.Any<CreateBillingCustomer>(), default)
            .Returns<BillingCustomer>(_ => throw new MaxioApiException(422, "Reference has already been taken"));
        _maxio.ListCustomerSubscriptionsAsync(7, default).Returns(new List<ShopperSubscription>());
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), default).Returns((ShopperSubscription?)null);
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), default)
            .Returns(new ShopperSubscription { Id = 5, State = "active", ProductHandle = "eshop-pro" });

        var service = CreateService();
        var result = await service.SubscribeAsync(new SubscribeShopperRequest
        {
            UserId = UserId,
            Email = Email,
            ProductHandle = "eshop-pro"
        });

        Assert.Equal(5, result.Id);
        await _maxio.Received(1).CreateSubscriptionAsync(Arg.Is<CreateBillingSubscription>(s => s.CustomerId == 7), default);
    }

    [Fact]
    public async Task RecoversWhenCreateSubscriptionLosesARace()
    {
        var customer = new BillingCustomer { Id = 42, Reference = UserId, Email = Email };
        var created = new ShopperSubscription { Id = 11, State = "active", ProductHandle = "eshop-pro" };

        _maxio.FindCustomerByReferenceAsync(UserId, default).Returns(customer);
        _maxio.ListCustomerSubscriptionsAsync(42, default)
            .Returns(new List<ShopperSubscription>(), new List<ShopperSubscription> { created });
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), default).Returns((ShopperSubscription?)null);
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), default)
            .Returns<ShopperSubscription>(_ => throw new MaxioApiException(422, "Reference must be unique"));

        var service = CreateService();
        var result = await service.SubscribeAsync(new SubscribeShopperRequest
        {
            UserId = UserId,
            Email = Email,
            ProductHandle = "eshop-pro"
        });

        Assert.Equal(11, result.Id);
    }

    [Fact]
    public async Task DefaultsToTheFirstAvailablePlanWhenHandleIsOmitted()
    {
        var customer = new BillingCustomer { Id = 42, Reference = UserId, Email = Email };
        _maxio.FindCustomerByReferenceAsync(UserId, default).Returns(customer);
        _maxio.ListCustomerSubscriptionsAsync(42, default).Returns(new List<ShopperSubscription>());
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), default).Returns((ShopperSubscription?)null);
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), default)
            .Returns(ci => new ShopperSubscription
            {
                Id = 3,
                State = "active",
                ProductHandle = ci.Arg<CreateBillingSubscription>().ProductHandle
            });

        var service = CreateService();
        var result = await service.SubscribeAsync(new SubscribeShopperRequest
        {
            UserId = UserId,
            Email = Email
        });

        Assert.Equal("eshop-pro", result.ProductHandle);
        await _maxio.Received().CreateSubscriptionAsync(Arg.Is<CreateBillingSubscription>(s => s.ProductHandle == "eshop-pro"), default);
    }

    [Fact]
    public async Task RejectsUnknownProductHandles()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => service.SubscribeAsync(new SubscribeShopperRequest
        {
            UserId = UserId,
            Email = Email,
            ProductHandle = "not-a-plan"
        }));

        Assert.Equal(400, ex.StatusCode);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), default);
    }

    [Fact]
    public async Task ListMySubscriptionsReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.FindCustomerByReferenceAsync(UserId, default).Returns((BillingCustomer?)null);
        var service = CreateService();

        var result = await service.ListMySubscriptionsAsync(UserId);

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), default);
    }

    [Theory]
    [InlineData("demouser@microsoft.com", "Demouser", "Subscriber")]
    [InlineData("jane.doe@example.com", "Jane", "Doe")]
    public void SplitsCustomerNamesFromEmail(string email, string first, string last)
    {
        var (actualFirst, actualLast) = SubscriptionService.SplitDisplayName(email, email);
        Assert.Equal(first, actualFirst);
        Assert.Equal(last, actualLast);
    }

    private SubscriptionService CreateService()
        => new(_maxio, _settings, _logger);
}
