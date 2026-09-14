using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly MaxioSubscriptionService _sut;

    public MaxioSubscriptionServiceTests()
    {
        var options = Options.Create(new MaxioOptions { ProductFamilyHandle = FamilyHandle, Subdomain = "test-site" });
        _sut = new MaxioSubscriptionService(_client, options);
    }

    private static MaxioProduct Plan(string handle, string name, long priceInCents, string familyHandle = FamilyHandle, bool archived = false) => new()
    {
        Id = 1,
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        ArchivedAt = archived ? System.DateTimeOffset.UtcNow : null,
        ProductFamily = new MaxioProductFamily { Handle = familyHandle }
    };

    [Fact]
    public async Task GetAvailablePlansAsync_OnlyReturnsActivePlansInConfiguredFamily()
    {
        _client.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct>
        {
            Plan("eshop-pro", "Pro Plan", 29900),
            Plan("other-family-plan", "Other", 500, familyHandle: "some-other-family"),
            Plan("archived-plan", "Archived", 100, archived: true)
        });

        var plans = await _sut.GetAvailablePlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299m, plan.Price);
    }

    [Fact]
    public async Task SubscribeAsync_UnknownPlanHandle_ThrowsWithoutCallingMaxio()
    {
        _client.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct> { Plan("eshop-pro", "Pro Plan", 29900) });

        await Assert.ThrowsAsync<UnknownSubscriptionPlanException>(() =>
            _sut.SubscribeAsync("user-1", "user@example.com", "First", "Last", "not-a-real-plan"));

        await _client.DidNotReceive().FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_NoExistingCustomerOrSubscription_CreatesBoth()
    {
        _client.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct> { Plan("eshop-pro", "Pro Plan", 29900) });
        _client.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = "user-1" });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 999, State = "active", Product = Plan("eshop-pro", "Pro Plan", 29900) });

        var result = await _sut.SubscribeAsync("user-1", "user@example.com", "First", "Last", "eshop-pro");

        Assert.Equal(999, result.SubscriptionId);
        await _client.Received(1).CreateCustomerAsync(Arg.Is<MaxioCreateCustomer>(c => c.Reference == "user-1"), Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(Arg.Is<MaxioCreateSubscription>(s => s.ProductHandle == "eshop-pro" && s.CustomerReference == "user-1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_CustomerAlreadyExists_DoesNotCreateANewOne()
    {
        _client.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct> { Plan("eshop-pro", "Pro Plan", 29900) });
        _client.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns(new MaxioCustomer { Id = 42, Reference = "user-1" });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 999, State = "active", Product = Plan("eshop-pro", "Pro Plan", 29900) });

        await _sut.SubscribeAsync("user-1", "user@example.com", "First", "Last", "eshop-pro");

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_LiveSubscriptionToSamePlanAlreadyExists_ReusesItInsteadOfCreatingADuplicate()
    {
        _client.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct> { Plan("eshop-pro", "Pro Plan", 29900) });
        _client.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns(new MaxioCustomer { Id = 42, Reference = "user-1" });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>
        {
            new() { Id = 555, State = "active", Product = Plan("eshop-pro", "Pro Plan", 29900) }
        });

        var result = await _sut.SubscribeAsync("user-1", "user@example.com", "First", "Last", "eshop-pro");

        Assert.Equal(555, result.SubscriptionId);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ConcurrentDoubleClickRacesCustomerCreation_RecoversByRefetching()
    {
        _client.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct> { Plan("eshop-pro", "Pro Plan", 29900) });
        _client.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, new MaxioCustomer { Id = 42, Reference = "user-1" });
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns<MaxioCustomer>(_ => throw new MaxioApiException(HttpStatusCode.UnprocessableEntity, "reference has already been taken"));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 999, State = "active", Product = Plan("eshop-pro", "Pro Plan", 29900) });

        var result = await _sut.SubscribeAsync("user-1", "user@example.com", "First", "Last", "eshop-pro");

        Assert.Equal(999, result.SubscriptionId);
        await _client.Received(2).FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptionsAsync_NoMaxioCustomerYet_ReturnsEmptyList()
    {
        _client.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var result = await _sut.GetSubscriptionsAsync("user-1");

        Assert.Empty(result);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }
}
