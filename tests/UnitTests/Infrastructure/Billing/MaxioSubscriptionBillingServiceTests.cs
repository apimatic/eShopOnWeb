using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string Family = "eshop-subscribe";
    private const string ProHandle = "eshop-pro";
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger = Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
    private readonly MaxioSubscriptionBillingService _sut;

    public MaxioSubscriptionBillingServiceTests()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = Family
        });

        _sut = new MaxioSubscriptionBillingService(_maxio, options, _logger);

        _maxio.ListProductsForProductFamilyAsync(Family, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new()
                {
                    Id = 1,
                    Handle = ProHandle,
                    Name = "Pro Plan",
                    Description = "Pro",
                    PriceInCents = 29900,
                    Interval = 1,
                    IntervalUnit = "month"
                },
                new()
                {
                    Id = 2,
                    Handle = "basic-plan",
                    Name = "Basic Plan",
                    PriceInCents = 2900,
                    Interval = 1,
                    IntervalUnit = "month"
                }
            });
    }

    [Fact]
    public async Task ListAvailablePlansAsync_ReturnsFamilyProductsOrderedByPrice()
    {
        var plans = await _sut.ListAvailablePlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle);
        Assert.Equal(29.00m, plans[0].Price);
        Assert.Equal(ProHandle, plans[1].Handle);
        Assert.Equal(299.00m, plans[1].Price);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerOnceThenReusesIt()
    {
        _maxio.ReadCustomerByReferenceAsync("demouser@microsoft.com", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, new MaxioCustomer { Id = 42, Reference = "demouser@microsoft.com" });
        _maxio.CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = "demouser@microsoft.com" });
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null);
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(CreatedPro(1001));

        await _sut.SubscribeAsync("demouser@microsoft.com", "demouser@microsoft.com", "demouser@microsoft.com", ProHandle);
        await _sut.SubscribeAsync("demouser@microsoft.com", "demouser@microsoft.com", "demouser@microsoft.com", ProHandle);

        await _maxio.Received(1).CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_DoesNotCreateASecondSubscriptionForTheSamePlan()
    {
        _maxio.ReadCustomerByReferenceAsync("demouser@microsoft.com", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = "demouser@microsoft.com" });
        _maxio.FindSubscriptionByReferenceAsync("demouser@microsoft.com:eshop-pro", Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null, CreatedPro(1001));
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>(), new[] { CreatedPro(1001) });
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(CreatedPro(1001));

        var first = await _sut.SubscribeAsync("demouser@microsoft.com", "demouser@microsoft.com", "demouser@microsoft.com", ProHandle);
        var second = await _sut.SubscribeAsync("demouser@microsoft.com", "demouser@microsoft.com", "demouser@microsoft.com", ProHandle);

        Assert.Equal(1001, first.Id);
        Assert.Equal(1001, second.Id);
        Assert.Equal("active", second.State);
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero), second.NextBillingDate);
        await _maxio.Received(1).CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsWhenPlanHandleIsUnknown()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            _sut.SubscribeAsync("demouser@microsoft.com", "demouser@microsoft.com", "demouser@microsoft.com", "not-a-plan"));
    }

    [Fact]
    public async Task ListSubscriptionsForBuyerAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.ReadCustomerByReferenceAsync("nobody", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var result = await _sut.ListSubscriptionsForBuyerAsync("nobody");

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private static MaxioSubscription CreatedPro(int id) =>
        new()
        {
            Id = id,
            State = "active",
            ProductPriceInCents = 29900,
            NextAssessmentAt = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero),
            Reference = "demouser@microsoft.com:eshop-pro",
            Product = new MaxioProduct
            {
                Handle = ProHandle,
                Name = "Pro Plan",
                PriceInCents = 29900
            }
        };
}
