using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class SubscriptionServiceTests
{
    private readonly IMaxioClient _maxioClient = Substitute.For<IMaxioClient>();
    private readonly IRepository<MaxioCustomer> _customerRepository = Substitute.For<IRepository<MaxioCustomer>>();
    private readonly IRepository<MaxioSubscription> _subscriptionRepository = Substitute.For<IRepository<MaxioSubscription>>();
    private readonly ILogger<SubscriptionService> _logger = Substitute.For<ILogger<SubscriptionService>>();

    private SubscriptionService CreateService()
    {
        var options = Options.Create(new MaxioOptions { ProductFamilyHandle = "eshop-subscribe" });
        return new SubscriptionService(_maxioClient, _customerRepository, _subscriptionRepository, options, _logger);
    }

    private static MaxioProductDto Product(string handle, string name, long priceInCents)
    {
        return new MaxioProductDto
        {
            Id = 1,
            Handle = handle,
            Name = name,
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month"
        };
    }

    private static MaxioSubscriptionDto Subscription(int id, string planHandle, string planName, long priceInCents, string state = "active")
    {
        return new MaxioSubscriptionDto
        {
            Id = id,
            State = state,
            ProductPriceInCents = priceInCents,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            Product = Product(planHandle, planName, priceInCents)
        };
    }

    [Fact]
    public async Task GetPlansAsync_ReturnsPlansFromMaxio()
    {
        _maxioClient.ListProductsAsync("eshop-subscribe", default)
            .Returns(new List<MaxioProductDto>
            {
                Product("eshop-pro", "Pro Plan", 29900),
                Product("basic-plan", "Basic Plan", 2900)
            });

        var service = CreateService();
        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle);
        Assert.Equal(29m, plans[0].Price);
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal(299m, plans[1].Price);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        _maxioClient.GetCustomerByReferenceAsync("user@example.com", default).Returns((MaxioCustomerDto?)null);
        _maxioClient.CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), default)
            .Returns(new MaxioCustomerDto { Id = 100, Reference = "user@example.com", Email = "user@example.com" });
        _maxioClient.ListCustomerSubscriptionsAsync(100, default).Returns(new List<MaxioSubscriptionDto>());
        _maxioClient.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), default)
            .Returns(Subscription(500, "eshop-pro", "Pro Plan", 29900));

        var service = CreateService();
        var result = await service.SubscribeAsync("user@example.com", "user@example.com", "eshop-pro");

        Assert.True(result.IsNew);
        Assert.Equal(500, result.SubscriptionId);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal(299m, result.Price);
        Assert.Equal("active", result.State);
        await _maxioClient.Received(1).CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), default);
        await _maxioClient.Received(1).CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), default);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscription_WhenAlreadySubscribed()
    {
        _maxioClient.GetCustomerByReferenceAsync("user@example.com", default)
            .Returns(new MaxioCustomerDto { Id = 100, Reference = "user@example.com" });
        _maxioClient.ListCustomerSubscriptionsAsync(100, default)
            .Returns(new List<MaxioSubscriptionDto> { Subscription(500, "eshop-pro", "Pro Plan", 29900) });

        var service = CreateService();
        var result = await service.SubscribeAsync("user@example.com", "user@example.com", "eshop-pro");

        Assert.False(result.IsNew);
        Assert.Equal(500, result.SubscriptionId);
        await _maxioClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), default);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscription_WhenLocalMappingExists()
    {
        _maxioClient.GetCustomerByReferenceAsync("user@example.com", default)
            .Returns(new MaxioCustomerDto { Id = 100, Reference = "user@example.com" });
        _subscriptionRepository.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<MaxioSubscription>>(), default)
            .Returns(new MaxioSubscription { UserId = "user@example.com", MaxioCustomerId = 100, MaxioSubscriptionId = 500, PlanHandle = "eshop-pro", State = "active" });
        _maxioClient.GetSubscriptionAsync(500, default)
            .Returns(Subscription(500, "eshop-pro", "Pro Plan", 29900));

        var service = CreateService();
        var result = await service.SubscribeAsync("user@example.com", "user@example.com", "eshop-pro");

        Assert.False(result.IsNew);
        Assert.Equal(500, result.SubscriptionId);
        await _maxioClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), default);
    }

    [Fact]
    public async Task SubscribeAsync_HandlesConflict_ByReturningExistingSubscription()
    {
        _maxioClient.GetCustomerByReferenceAsync("user@example.com", default)
            .Returns(new MaxioCustomerDto { Id = 100, Reference = "user@example.com" });
        _maxioClient.ListCustomerSubscriptionsAsync(100, default)
            .Returns(new List<MaxioSubscriptionDto>());
        _maxioClient.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), default)
            .Returns<Task<MaxioSubscriptionDto>>(_ => throw new MaxioApiException(HttpStatusCode.Conflict, "{\"errors\":[\"DuplicatePrevention::DuplicateSubmissionError\"]}", "duplicate"));
        _maxioClient.ListCustomerSubscriptionsAsync(100, default)
            .Returns(new List<MaxioSubscriptionDto> { Subscription(500, "eshop-pro", "Pro Plan", 29900) });

        var service = CreateService();
        var result = await service.SubscribeAsync("user@example.com", "user@example.com", "eshop-pro");

        Assert.False(result.IsNew);
        Assert.Equal(500, result.SubscriptionId);
    }

    [Fact]
    public async Task GetMySubscriptionsAsync_ReturnsEmpty_WhenNoCustomerExists()
    {
        _maxioClient.GetCustomerByReferenceAsync("user@example.com", default).Returns((MaxioCustomerDto?)null);

        var service = CreateService();
        var result = await service.GetMySubscriptionsAsync("user@example.com");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMySubscriptionsAsync_ReturnsSubscriptions_WhenCustomerExists()
    {
        _maxioClient.GetCustomerByReferenceAsync("user@example.com", default)
            .Returns(new MaxioCustomerDto { Id = 100, Reference = "user@example.com" });
        _maxioClient.ListCustomerSubscriptionsAsync(100, default)
            .Returns(new List<MaxioSubscriptionDto>
            {
                Subscription(500, "eshop-pro", "Pro Plan", 29900),
                Subscription(501, "basic-plan", "Basic Plan", 2900)
            });

        var service = CreateService();
        var result = await service.GetMySubscriptionsAsync("user@example.com");

        Assert.Equal(2, result.Count);
        Assert.Equal(500, result[0].SubscriptionId);
        Assert.Equal(501, result[1].SubscriptionId);
    }
}
