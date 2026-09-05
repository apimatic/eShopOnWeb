using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio.MaxioSubscriptionServiceTests;

public class SubscribeAsync
{
    private const string BuyerId = "buyer@example.com";
    private const string PlanHandle = "eshop-pro";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly MaxioSubscriptionService _sut;

    public SubscribeAsync()
    {
        _sut = new MaxioSubscriptionService(_client, Options.Create(new MaxioOptions
        {
            ApiKey = "unused",
            Subdomain = "unused",
            ProductFamilyHandle = "eshop-subscribe"
        }));
    }

    [Fact]
    public async Task CreatesCustomerAndSubscription_WhenBuyerHasNeitherYet()
    {
        _client.FindCustomerByReferenceAsync(BuyerId, Arg.Any<CancellationToken>()).Returns((MaxioCustomerModel?)null);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerModel { Id = 1, Reference = BuyerId });
        _client.ListCustomerSubscriptionsAsync(1, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionModel>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscriptionModel
            {
                Id = 42,
                State = "active",
                Product = new MaxioProductModel { Handle = PlanHandle, Name = "Pro Plan", PriceInCents = 29900 }
            });

        var result = await _sut.SubscribeAsync(BuyerId, BuyerId, PlanHandle);

        Assert.Equal(42, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal(PlanHandle, result.PlanHandle);
        await _client.Received(1).CreateCustomerAsync(Arg.Any<MaxioCreateCustomerAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingSubscription_WithoutCallingCreate_WhenBuyerAlreadySubscribedToPlan()
    {
        _client.FindCustomerByReferenceAsync(BuyerId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerModel { Id = 7, Reference = BuyerId });
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionModel>
            {
                new()
                {
                    Id = 99,
                    State = "active",
                    Product = new MaxioProductModel { Handle = PlanHandle, Name = "Pro Plan", PriceInCents = 29900 }
                }
            });

        var result = await _sut.SubscribeAsync(BuyerId, BuyerId, PlanHandle);

        Assert.Equal(99, result.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomerAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsWinningSubscription_WhenCreateConflictsWithConcurrentDoubleClick()
    {
        var customer = new MaxioCustomerModel { Id = 7, Reference = BuyerId };
        var winningSubscription = new MaxioSubscriptionModel
        {
            Id = 55,
            State = "active",
            Product = new MaxioProductModel { Handle = PlanHandle, Name = "Pro Plan", PriceInCents = 29900 }
        };

        _client.FindCustomerByReferenceAsync(BuyerId, Arg.Any<CancellationToken>()).Returns(customer);
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionModel>(), new List<MaxioSubscriptionModel> { winningSubscription });
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<MaxioSubscriptionModel>(_ => throw new MaxioApiException(HttpStatusCode.Conflict, "DuplicatePrevention::DuplicateSubmissionError"));

        var result = await _sut.SubscribeAsync(BuyerId, BuyerId, PlanHandle);

        Assert.Equal(55, result.Id);
    }

    [Fact]
    public async Task ReusesExistingCustomer_WhenCreateConflictsWithConcurrentDoubleClick()
    {
        var raceWinnerCustomer = new MaxioCustomerModel { Id = 3, Reference = BuyerId };

        _client.FindCustomerByReferenceAsync(BuyerId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomerModel?)null, raceWinnerCustomer);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<MaxioCustomerModel>(_ => throw new MaxioApiException(HttpStatusCode.UnprocessableEntity, "Reference: must be unique - that value has been taken."));
        _client.ListCustomerSubscriptionsAsync(3, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionModel>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscriptionModel
            {
                Id = 88,
                State = "active",
                Product = new MaxioProductModel { Handle = PlanHandle, Name = "Pro Plan", PriceInCents = 29900 }
            });

        var result = await _sut.SubscribeAsync(BuyerId, BuyerId, PlanHandle);

        Assert.Equal(88, result.Id);
    }
}
