using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services.MaxioBillingServiceTests;

public class SubscribeAsync
{
    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly MaxioBillingService _sut;

    public SubscribeAsync()
    {
        _sut = new MaxioBillingService(_client, new MaxioBuyerLock(), Options.Create(new MaxioSettings
        {
            ProductFamilyHandle = "eshop-subscribe"
        }));

        _client.FindProductByHandleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioProduct { Handle = "eshop-pro", RequireCreditCard = false });
    }

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNeitherExist()
    {
        _client.FindCustomerByReferenceAsync("buyer@example.com", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer)null!);
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerAttributes>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Email = "buyer@example.com", Reference = "buyer@example.com" });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionAttributes>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription
            {
                Id = 99,
                State = "active",
                Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 }
            });

        var result = await _sut.SubscribeAsync(new SubscriptionEnrollmentRequest("buyer@example.com", "buyer@example.com", "eshop-pro"));

        Assert.Equal(99, result.MaxioSubscriptionId);
        Assert.Equal(42, result.MaxioCustomerId);
        Assert.Equal("active", result.State);
        Assert.Equal(299.00m, result.Price);
        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<CreateCustomerAttributes>(a => a.Reference == "buyer@example.com" && a.Email == "buyer@example.com"),
            Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionAttributes>(a => a.ProductHandle == "eshop-pro" && a.CustomerId == 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReusesExistingCustomerInsteadOfCreatingANewOne()
    {
        _client.FindCustomerByReferenceAsync("buyer@example.com", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 7, Reference = "buyer@example.com" });
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionAttributes>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 1, State = "active", Product = new MaxioProduct { Handle = "eshop-pro" } });

        await _sut.SubscribeAsync(new SubscriptionEnrollmentRequest("buyer@example.com", "buyer@example.com", "eshop-pro"));

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomerAttributes>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoubleClickReturnsExistingSubscriptionInsteadOfCreatingADuplicate()
    {
        _client.FindCustomerByReferenceAsync("buyer@example.com", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 7, Reference = "buyer@example.com" });
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new() { Id = 55, State = "active", Product = new MaxioProduct { Handle = "eshop-pro" } }
            });

        var result = await _sut.SubscribeAsync(new SubscriptionEnrollmentRequest("buyer@example.com", "buyer@example.com", "eshop-pro"));

        Assert.Equal(55, result.MaxioSubscriptionId);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionAttributes>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatesANewSubscriptionWhenTheOnlyExistingOneIsCanceled()
    {
        _client.FindCustomerByReferenceAsync("buyer@example.com", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 7, Reference = "buyer@example.com" });
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new() { Id = 1, State = "canceled", Product = new MaxioProduct { Handle = "eshop-pro" } }
            });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionAttributes>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 2, State = "active", Product = new MaxioProduct { Handle = "eshop-pro" } });

        var result = await _sut.SubscribeAsync(new SubscriptionEnrollmentRequest("buyer@example.com", "buyer@example.com", "eshop-pro"));

        Assert.Equal(2, result.MaxioSubscriptionId);
    }

    [Fact]
    public async Task ThrowsA404WhenThePlanHandleDoesNotExist()
    {
        _client.FindProductByHandleAsync("no-such-plan", Arg.Any<CancellationToken>())
            .Returns((MaxioProduct)null!);

        var ex = await Assert.ThrowsAsync<MaxioApiException>(() =>
            _sut.SubscribeAsync(new SubscriptionEnrollmentRequest("buyer@example.com", "buyer@example.com", "no-such-plan")));

        Assert.Equal(404, ex.StatusCode);
        await _client.DidNotReceive().FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsesRemittanceCollectionWhenThePlanDoesNotRequireACard()
    {
        _client.FindCustomerByReferenceAsync("buyer@example.com", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 7, Reference = "buyer@example.com" });
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionAttributes>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 1, State = "active", Product = new MaxioProduct { Handle = "eshop-pro" } });

        await _sut.SubscribeAsync(new SubscriptionEnrollmentRequest("buyer@example.com", "buyer@example.com", "eshop-pro"));

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionAttributes>(a => a.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoversWhenCustomerCreationRacesAndMaxioReportsAConflict()
    {
        _client.FindCustomerByReferenceAsync("buyer@example.com", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer)null!, new MaxioCustomer { Id = 7, Reference = "buyer@example.com" });
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerAttributes>(), Arg.Any<CancellationToken>())
            .Returns<MaxioCustomer>(_ => throw new MaxioApiException(422, "reference has already been taken"));
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionAttributes>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 9, State = "active", Product = new MaxioProduct { Handle = "eshop-pro" } });

        var result = await _sut.SubscribeAsync(new SubscriptionEnrollmentRequest("buyer@example.com", "buyer@example.com", "eshop-pro"));

        Assert.Equal(9, result.MaxioSubscriptionId);
        await _client.Received(2).FindCustomerByReferenceAsync("buyer@example.com", Arg.Any<CancellationToken>());
    }
}
