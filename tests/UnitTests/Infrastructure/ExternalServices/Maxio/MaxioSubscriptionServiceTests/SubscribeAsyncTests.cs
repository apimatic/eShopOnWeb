using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.ExternalServices.Maxio;
using Microsoft.eShopWeb.Infrastructure.ExternalServices.Maxio.Wire;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.ExternalServices.Maxio.MaxioSubscriptionServiceTests;

/// <summary>
/// Covers the idempotency guarantees the hero flow depends on: a double-click subscribe must
/// never create a second Maxio customer or a second subscription for the same plan.
/// </summary>
public class SubscribeAsyncTests
{
    private const string BuyerEmail = "buyer@example.com";
    private const string ProductFamilyHandle = "eshop-subscribe";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly MaxioSubscriptionService _sut;

    public SubscribeAsyncTests()
    {
        var options = Options.Create(new MaxioOptions { ProductFamilyHandle = ProductFamilyHandle });
        _sut = new MaxioSubscriptionService(_client, options);

        _client.ListProductsForFamilyAsync(ProductFamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Id = 1, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
            });
    }

    [Fact]
    public async Task CreatesCustomerAndSubscription_WhenBuyerIsNew()
    {
        _client.FindCustomerByReferenceAsync(BuyerEmail, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync("buyer", "Customer", BuyerEmail, BuyerEmail, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = BuyerEmail });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 999, State = "active", Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 } });

        var result = await _sut.SubscribeAsync(BuyerEmail, "eshop-pro");

        Assert.Equal(999, result.MaxioSubscriptionId);
        await _client.Received(1).CreateCustomerAsync("buyer", "Customer", BuyerEmail, BuyerEmail, Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotCreateAnotherCustomer_WhenOneAlreadyExistsForBuyer()
    {
        _client.FindCustomerByReferenceAsync(BuyerEmail, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = BuyerEmail });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 999, State = "active", Product = new MaxioProduct { Handle = "eshop-pro" } });

        await _sut.SubscribeAsync(BuyerEmail, "eshop-pro");

        await _client.DidNotReceive().CreateCustomerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingSubscription_WhenBuyerAlreadyHasLiveSubscriptionToPlan_InsteadOfCreatingADuplicate()
    {
        _client.FindCustomerByReferenceAsync(BuyerEmail, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = BuyerEmail });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new()
                {
                    Id = 555,
                    State = "active",
                    Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 }
                }
            });

        var result = await _sut.SubscribeAsync(BuyerEmail, "eshop-pro");

        Assert.Equal(555, result.MaxioSubscriptionId);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatesNewSubscription_WhenExistingOneForThatPlanIsCanceled()
    {
        _client.FindCustomerByReferenceAsync(BuyerEmail, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = BuyerEmail });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new() { Id = 555, State = "canceled", Product = new MaxioProduct { Handle = "eshop-pro" } }
            });
        _client.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 777, State = "active", Product = new MaxioProduct { Handle = "eshop-pro" } });

        var result = await _sut.SubscribeAsync(BuyerEmail, "eshop-pro");

        Assert.Equal(777, result.MaxioSubscriptionId);
    }

    [Fact]
    public async Task ThrowsSubscriptionPlanNotFoundException_ForAHandleNotInTheProductFamily()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => _sut.SubscribeAsync(BuyerEmail, "not-a-real-plan"));

        await _client.DidNotReceive().FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoversByLookingUpTheCustomer_WhenCreateCustomerFailsBecauseOneWasCreatedConcurrently()
    {
        _client.FindCustomerByReferenceAsync(BuyerEmail, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, new MaxioCustomer { Id = 42, Reference = BuyerEmail });
        _client.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<MaxioCustomer>(_ => throw new MaxioApiException(422, new[] { "Reference has already been taken" }));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 999, State = "active", Product = new MaxioProduct { Handle = "eshop-pro" } });

        var result = await _sut.SubscribeAsync(BuyerEmail, "eshop-pro");

        Assert.Equal(999, result.MaxioSubscriptionId);
        await _client.Received(2).FindCustomerByReferenceAsync(BuyerEmail, Arg.Any<CancellationToken>());
    }
}
