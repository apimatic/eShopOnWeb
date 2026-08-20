using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class MaxioSubscriptionBillingServiceTests
{
    private readonly IMaxioApiClient _maxio = Substitute.For<IMaxioApiClient>();
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger =
        Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
    private readonly MaxioSubscriptionBillingService _sut;

    public MaxioSubscriptionBillingServiceTests()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = "eshop-subscribe"
        });
        _sut = new MaxioSubscriptionBillingService(_maxio, options, _logger);
    }

    [Fact]
    public async Task ListPlansAsync_MapsPriceFromCents()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
            new() { Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" }
        });

        var plans = await _sut.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle);
        Assert.Equal(29.00m, plans[0].Price);
        Assert.Equal(299.00m, plans[1].Price);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
        });
        _maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = "user-1" });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<CancellationToken>()).Returns(new MaxioSubscription
        {
            Id = 99,
            State = "active",
            ProductPriceInCents = 29900,
            Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
        });

        var result = await _sut.SubscribeAsync("user-1", "demouser@microsoft.com", "demouser@microsoft.com", "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(99, result.Id);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal(299.00m, result.Price);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomer>(c => c.Reference == "user-1" && c.Email == "demouser@microsoft.com"),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotent_WhenLiveSubscriptionAlreadyExists()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
        });
        _maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = "user-1" });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>
        {
            new()
            {
                Id = 99,
                State = "active",
                ProductPriceInCents = 29900,
                Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
            }
        });

        var result = await _sut.SubscribeAsync("user-1", "demouser@microsoft.com", "demouser@microsoft.com", "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(99, result.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ReusesCustomer_OnCreateConflict()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
        });
        _maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<MaxioCustomer?>(null),
                Task.FromResult<MaxioCustomer?>(new MaxioCustomer { Id = 42, Reference = "user-1" }));
        _maxio.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns<MaxioCustomer>(_ => throw new BillingException("reference already taken", 400));
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<CancellationToken>()).Returns(new MaxioSubscription
        {
            Id = 99,
            State = "active",
            ProductPriceInCents = 29900,
            Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
        });

        var result = await _sut.SubscribeAsync("user-1", "demouser@microsoft.com", "demouser@microsoft.com", "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(99, result.Id);
    }

    [Fact]
    public async Task SubscribeAsync_Throws_WhenPlanIsUnknown()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
        });

        var ex = await Assert.ThrowsAsync<BillingException>(() =>
            _sut.SubscribeAsync("user-1", "a@b.com", "a@b.com", "not-a-plan"));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task ListSubscriptionsForUserAsync_ReturnsEmpty_WhenCustomerMissing()
    {
        _maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var result = await _sut.ListSubscriptionsForUserAsync("user-1");

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("demouser@microsoft.com", "demouser")]
    [InlineData("shopper", "shopper")]
    public void SplitDisplayName_UsesEmailLocalPart(string source, string expectedFirst)
    {
        var (first, last) = MaxioSubscriptionBillingService.SplitDisplayName(source, source);
        Assert.Equal(expectedFirst, first);
        Assert.Equal("eShopOnWeb", last);
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("canceled", false)]
    [InlineData("expired", false)]
    [InlineData("past_due", true)]
    public void IsLive_RecognizesCurrentSubscriptionStates(string state, bool expected)
    {
        Assert.Equal(expected, MaxioSubscriptionBillingService.IsLive(state));
    }
}
