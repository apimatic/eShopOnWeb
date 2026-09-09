using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string ProReference = "shopper@example.com";

    private readonly IMaxioClient _client = Substitute.For<IMaxioClient>();
    private readonly ILogger<MaxioSubscriptionService> _logger = Substitute.For<ILogger<MaxioSubscriptionService>>();
    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = FamilyHandle,
    };

    private MaxioSubscriptionService CreateService() => new(_client, _settings, _logger);

    private static MaxioProduct ProProduct() =>
        new(7126957, "eshop-pro", "Pro Plan", "The pro plan", 29900, 1, "month",
            new MaxioProductFamily(3023074, FamilyHandle, "eShop Subscribe"));

    private static MaxioSubscription Subscription(int id, string handle, string state) =>
        new(id, state, handle == "eshop-pro" ? 29900 : 2900, "remittance",
            DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow,
            new MaxioProduct(1, handle, "Plan", null, 29900, 1, "month", null), null);

    private SubscribeCommand ProCommand() =>
        new(ProReference, ProReference, "Shopper", "Example", "eshop-pro");

    private void ArrangePlans() =>
        _client.ListProductsForFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { ProProduct() });

    [Fact]
    public async Task GetPlansAsync_MapsProductsToPlans()
    {
        ArrangePlans();
        var service = CreateService();

        var plans = await service.GetPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("$299.00", plan.FormattedPrice);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        ArrangePlans();
        _client.FindCustomerByReferenceAsync(ProReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerBody>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(500, ProReference, "Shopper", "Example", ProReference));
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(9001, "eshop-pro", "active"));

        var service = CreateService();
        var result = await service.SubscribeAsync(ProCommand());

        Assert.False(result.AlreadyExisted);
        Assert.True(result.CustomerCreated);
        Assert.Equal(500, result.CustomerId);
        Assert.Equal(9001, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);

        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<CreateCustomerBody>(c => c.Reference == ProReference && c.Email == ProReference),
            Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionBody>(s =>
                s.ProductHandle == "eshop-pro" && s.CustomerId == 500 && s.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotent_WhenActiveSubscriptionExists()
    {
        ArrangePlans();
        _client.FindCustomerByReferenceAsync(ProReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(500, ProReference, "Shopper", "Example", ProReference));
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(9001, "eshop-pro", "active") });

        var service = CreateService();
        var result = await service.SubscribeAsync(ProCommand());

        Assert.True(result.AlreadyExisted);
        Assert.False(result.CustomerCreated);
        Assert.Equal(9001, result.Subscription.Id);

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomerBody>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_CreatesNew_WhenOnlyCanceledSubscriptionExists()
    {
        ArrangePlans();
        _client.FindCustomerByReferenceAsync(ProReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(500, ProReference, "Shopper", "Example", ProReference));
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(9001, "eshop-pro", "canceled") });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(9002, "eshop-pro", "active"));

        var service = CreateService();
        var result = await service.SubscribeAsync(ProCommand());

        Assert.False(result.AlreadyExisted);
        Assert.Equal(9002, result.Subscription.Id);
        await _client.Received(1).CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ResolvesRace_WhenCustomerCreateConflicts()
    {
        ArrangePlans();
        // First lookup: not found (drives a create). Second lookup (in the catch): a concurrent request won.
        _client.FindCustomerByReferenceAsync(ProReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, new MaxioCustomer(500, ProReference, "Shopper", "Example", ProReference));
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerBody>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(HttpStatusCode.UnprocessableEntity,
                new[] { "Reference: must be unique." }, null));
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(9003, "eshop-pro", "active"));

        var service = CreateService();
        var result = await service.SubscribeAsync(ProCommand());

        Assert.False(result.CustomerCreated);
        Assert.Equal(500, result.CustomerId);
        Assert.Equal(9003, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeAsync_Throws_WhenPlanHandleUnknown()
    {
        ArrangePlans();
        var service = CreateService();
        var command = new SubscribeCommand(ProReference, ProReference, "Shopper", "Example", "does-not-exist");

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => service.SubscribeAsync(command));
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_WrapsUpstreamFailure_AsBillingException()
    {
        ArrangePlans();
        _client.FindCustomerByReferenceAsync(ProReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(500, ProReference, "Shopper", "Example", ProReference));
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(HttpStatusCode.UnprocessableEntity,
                new[] { "No payment method was on file." }, null));

        var service = CreateService();

        await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.SubscribeAsync(ProCommand()));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsEmpty_WhenNoCustomer()
    {
        _client.FindCustomerByReferenceAsync(ProReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var service = CreateService();
        var subscriptions = await service.GetSubscriptionsAsync(ProReference);

        Assert.Empty(subscriptions);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptionsAsync_MapsAndOrders_ByCreatedDescending()
    {
        _client.FindCustomerByReferenceAsync(ProReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(500, ProReference, "Shopper", "Example", ProReference));
        var older = Subscription(1, "basic-plan", "active") with { CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        var newer = Subscription(2, "eshop-pro", "active") with { CreatedAt = DateTimeOffset.UtcNow };
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { older, newer });

        var service = CreateService();
        var subscriptions = new List<ShopperSubscription>(await service.GetSubscriptionsAsync(ProReference));

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal(2, subscriptions[0].Id);
        Assert.Equal(1, subscriptions[1].Id);
    }

    [Fact]
    public async Task Methods_Throw_WhenNotConfigured()
    {
        var service = new MaxioSubscriptionService(_client, new MaxioSettings(), _logger);

        await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.GetPlansAsync());
    }
}
