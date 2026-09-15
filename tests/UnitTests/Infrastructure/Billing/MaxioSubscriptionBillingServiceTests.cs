using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing.MaxioModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly ILogger<MaxioSubscriptionBillingService> _logger = Substitute.For<ILogger<MaxioSubscriptionBillingService>>();
    private readonly ShopperIdentity _shopper = new("demouser@microsoft.com", "Demouser", "Shopper");

    private MaxioSubscriptionBillingService CreateSut()
    {
        var options = Options.Create(new MaxioOptions { ProductFamilyHandle = "eshop-subscribe" });
        return new MaxioSubscriptionBillingService(_maxio, options, _logger);
    }

    [Fact]
    public async Task ListAvailablePlansAsync_OmitsArchivedProducts()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<ProductDto>
            {
                new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
                new() { Handle = "old-plan", Name = "Old", PriceInCents = 100, ArchivedAt = DateTimeOffset.UtcNow }
            });

        var plans = await CreateSut().ListAvailablePlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<ProductDto>
            {
                new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
            });
        _maxio.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CustomerDto?)null);
        _maxio.CreateCustomerAsync(Arg.Any<CreateCustomerBody>(), Arg.Any<CancellationToken>())
            .Returns(new CustomerDto { Id = 42, Email = _shopper.Email, Reference = "eshoponweb:demouser@microsoft.com" });
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SubscriptionDto?)null);
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionDto
            {
                Id = 99,
                State = "active",
                ProductPriceInCents = 29900,
                NextAssessmentAt = DateTimeOffset.Parse("2026-09-19T00:00:00Z"),
                Product = new ProductDto { Handle = "eshop-pro", Name = "Pro Plan" }
            });

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal(299.00m, result.Price);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<CreateCustomerBody>(c => c.Reference == "eshoponweb:demouser@microsoft.com" && c.Email == _shopper.Email),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionBody>(s =>
                s.ProductHandle == "eshop-pro"
                && s.CustomerId == 42
                && s.PaymentCollectionMethod == "remittance"
                && s.Reference == "eshoponweb:demouser@microsoft.com:eshop-pro"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentWhenSubscriptionAlreadyExists()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<ProductDto>
            {
                new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
            });
        _maxio.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CustomerDto { Id = 42, Email = _shopper.Email });
        _maxio.FindSubscriptionByReferenceAsync("eshoponweb:demouser@microsoft.com:eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new SubscriptionDto
            {
                Id = 99,
                State = "active",
                ProductPriceInCents = 29900,
                Product = new ProductDto { Handle = "eshop-pro", Name = "Pro Plan" }
            });

        var first = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");
        var second = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(first.Id, second.Id);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomerBody>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsWhenPlanIsUnknown()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<ProductDto>
            {
                new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
            });

        await Assert.ThrowsAsync<PlanNotFoundException>(() => CreateSut().SubscribeAsync(_shopper, "not-a-plan"));
    }

    [Fact]
    public async Task ListSubscriptionsForShopperAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CustomerDto?)null);

        var result = await CreateSut().ListSubscriptionsForShopperAsync(_shopper);

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void BuildCustomerReference_IsStableAndLowercase()
    {
        Assert.Equal(
            "eshoponweb:demouser@microsoft.com",
            MaxioSubscriptionBillingService.BuildCustomerReference("  DemoUser@Microsoft.com "));
    }
}
