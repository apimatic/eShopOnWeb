using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private readonly IMaxioBillingGateway _maxio = Substitute.For<IMaxioBillingGateway>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly SubscribeToPlanRequest _request = new()
    {
        UserId = "user-1",
        Email = "demouser@microsoft.com",
        FirstName = "Demo",
        LastName = "User",
        ProductHandle = "eshop-pro"
    };

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenShopperIsNew()
    {
        _maxio.FindCustomerByReferenceAsync(_request.UserId).Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync(_request.UserId, _request.Email, _request.FirstName, _request.LastName, Arg.Any<string>())
            .Returns(new BillingCustomer { Id = 42, Reference = _request.UserId, Email = _request.Email });
        _maxio.ListCustomerSubscriptionsAsync(42).Returns(new List<CustomerSubscription>());
        _maxio.CreateSubscriptionAsync(42, _request.ProductHandle, Arg.Any<string>())
            .Returns(new CustomerSubscription
            {
                Id = 99,
                State = "active",
                ProductHandle = _request.ProductHandle,
                ProductName = "Pro Plan",
                Price = 299m
            });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_request);

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        await _maxio.Received(1).CreateCustomerAsync(
            _request.UserId, _request.Email, _request.FirstName, _request.LastName, Arg.Any<string>());
        await _maxio.Received(1).CreateSubscriptionAsync(42, _request.ProductHandle, Arg.Any<string>());
    }

    [Fact]
    public async Task ReusesExistingCustomerAndDoesNotCreateASecondLiveSubscription()
    {
        var existing = new CustomerSubscription
        {
            Id = 77,
            State = "active",
            ProductHandle = "eshop-pro",
            ProductName = "Pro Plan",
            Price = 299m
        };
        _maxio.FindCustomerByReferenceAsync(_request.UserId)
            .Returns(new BillingCustomer { Id = 42, Reference = _request.UserId, Email = _request.Email });
        _maxio.ListCustomerSubscriptionsAsync(42).Returns(new List<CustomerSubscription> { existing });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var first = await service.SubscribeAsync(_request);
        var second = await service.SubscribeAsync(_request);

        Assert.Equal(77, first.Id);
        Assert.Equal(77, second.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RecoversWhenCustomerCreateRacesOnReference()
    {
        _maxio.FindCustomerByReferenceAsync(_request.UserId)
            .Returns((BillingCustomer?)null, new BillingCustomer { Id = 42, Reference = _request.UserId, Email = _request.Email });
        _maxio.CreateCustomerAsync(_request.UserId, _request.Email, _request.FirstName, _request.LastName, Arg.Any<string>())
            .Returns<BillingCustomer>(_ => throw new DuplicateException("reference already taken"));
        _maxio.ListCustomerSubscriptionsAsync(42).Returns(new List<CustomerSubscription>());
        _maxio.CreateSubscriptionAsync(42, _request.ProductHandle, Arg.Any<string>())
            .Returns(new CustomerSubscription { Id = 11, State = "active", ProductHandle = _request.ProductHandle, Price = 29m });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_request);

        Assert.Equal(11, result.Id);
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("trialing", true)]
    [InlineData("past_due", true)]
    [InlineData("canceled", false)]
    [InlineData("expired", false)]
    [InlineData("trial_ended", false)]
    public void TreatsExpectedStatesAsLiveOrTerminal(string state, bool expectedLive)
    {
        Assert.Equal(expectedLive, SubscriptionBillingService.IsLive(state));
    }
}
