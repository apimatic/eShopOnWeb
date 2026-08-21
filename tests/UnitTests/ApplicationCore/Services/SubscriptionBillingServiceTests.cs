using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class SubscriptionBillingServiceTests
{
    private readonly IMaxioBillingGateway _gateway = Substitute.For<IMaxioBillingGateway>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe"
    };
    private readonly ShopperIdentity _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");

    private SubscriptionBillingService CreateSut() => new(_gateway, _settings, _logger);

    [Fact]
    public async Task ListPlansAsync_ReturnsFamilyPlansSortedByPrice()
    {
        _gateway.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan>
            {
                Plan("eshop-pro", "Pro", 29900),
                Plan("basic-plan", "Basic", 2900),
                Plan("", "Ignored", 100)
            });

        var result = await CreateSut().ListPlansAsync(default);

        Assert.Collection(result,
            first => Assert.Equal("basic-plan", first.Handle),
            second => Assert.Equal("eshop-pro", second.Handle));
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription()
    {
        ArrangeValidPlan("eshop-pro");
        _gateway.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _gateway.CreateCustomerAsync(Arg.Any<NewMaxioCustomer>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(42, _shopper.UserId, _shopper.Email, "Demouser", "Shopper"));
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CustomerSubscription?)null);
        _gateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        var created = Sub(1001, "active", "eshop-pro");
        _gateway.CreateSubscriptionAsync(Arg.Any<NewMaxioSubscription>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(created);

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro", default);

        Assert.True(result.Created);
        Assert.Equal(1001, result.Subscription.Id);
        await _gateway.Received(1).CreateCustomerAsync(
            Arg.Is<NewMaxioCustomer>(c => c.Reference == _shopper.UserId && c.Email == _shopper.Email),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewMaxioSubscription>(s => s.CustomerId == 42 && s.ProductHandle == "eshop-pro"),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ReusesExistingCustomerAndLiveSubscription()
    {
        ArrangeValidPlan("eshop-pro");
        _gateway.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(42, _shopper.UserId, _shopper.Email, "Demo", "Shopper"));
        var existing = Sub(77, "active", "eshop-pro");
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro", default);

        Assert.False(result.Created);
        Assert.Equal(77, result.Subscription.Id);
        await _gateway.DidNotReceive().CreateCustomerAsync(Arg.Any<NewMaxioCustomer>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<NewMaxioSubscription>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentWhenMaxioReportsConflict()
    {
        ArrangeValidPlan("eshop-pro");
        _gateway.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(42, _shopper.UserId, _shopper.Email, "Demo", "Shopper"));
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CustomerSubscription?)null, Sub(88, "active", "eshop-pro"));
        _gateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewMaxioSubscription>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<CustomerSubscription>(_ => throw new BillingConflictException("duplicate"));

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro", default);

        Assert.False(result.Created);
        Assert.Equal(88, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsPlansOutsideConfiguredFamily()
    {
        _gateway.GetProductByHandleAsync("other-plan", Arg.Any<CancellationToken>())
            .Returns(new MaxioProduct(Plan("other-plan", "Other", 1000), "some-other-family"));

        await Assert.ThrowsAsync<UnknownSubscriptionPlanException>(
            () => CreateSut().SubscribeAsync(_shopper, "other-plan", default));
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        _gateway.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var result = await CreateSut().ListMySubscriptionsAsync(_shopper, default);

        Assert.Empty(result);
        await _gateway.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private void ArrangeValidPlan(string handle)
    {
        _gateway.GetProductByHandleAsync(handle, Arg.Any<CancellationToken>())
            .Returns(new MaxioProduct(Plan(handle, handle, 29900), _settings.ProductFamilyHandle));
    }

    private static SubscriptionPlan Plan(string handle, string name, long cents) =>
        new(handle, name, null, cents / 100m, cents, 1, "month");

    private static CustomerSubscription Sub(int id, string state, string handle) =>
        new(id, state, handle, handle, 299m, 29900, DateTimeOffset.UtcNow.AddMonths(1), $"eshop:user-1:{handle}");
}
