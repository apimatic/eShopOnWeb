using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class SubscribeAsync
{
    private const string UserName = "buyer@example.com";
    private const string ProductFamilyHandle = "eshop-subscribe";

    private readonly IMaxioClient _mockMaxioClient = Substitute.For<IMaxioClient>();
    private readonly MaxioOptions _maxioOptions = new() { ProductFamilyHandle = ProductFamilyHandle };

    private readonly MaxioPlan _proPlan = new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" };

    public SubscribeAsync()
    {
        _mockMaxioClient.ListPlansAsync(ProductFamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioPlan> { _proPlan });
    }

    [Fact]
    public async Task WhenNoExistingSubscription_CreatesCustomerAndSubscription()
    {
        var customer = new MaxioCustomer { Id = 42, Reference = UserName };
        _mockMaxioClient.EnsureCustomerAsync(UserName, UserName, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(customer);
        _mockMaxioClient.ListCustomerSubscriptionsAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        var created = new MaxioSubscription { Id = 1001, CustomerId = customer.Id, ProductHandle = _proPlan.Handle, State = "active" };
        _mockMaxioClient.CreateSubscriptionAsync(customer.Id, _proPlan.Handle, Arg.Any<CancellationToken>())
            .Returns(created);

        var sut = new Microsoft.eShopWeb.ApplicationCore.Services.SubscriptionService(_mockMaxioClient, _maxioOptions);

        var result = await sut.SubscribeAsync(UserName, _proPlan.Handle);

        Assert.Equal(created.Id, result.Id);
        await _mockMaxioClient.Received(1).CreateSubscriptionAsync(customer.Id, _proPlan.Handle, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenLiveSubscriptionToSamePlanAlreadyExists_ReturnsExisting_AndDoesNotCreateAnother()
    {
        var customer = new MaxioCustomer { Id = 42, Reference = UserName };
        _mockMaxioClient.EnsureCustomerAsync(UserName, UserName, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(customer);
        var existing = new MaxioSubscription { Id = 999, CustomerId = customer.Id, ProductHandle = _proPlan.Handle, State = "active" };
        _mockMaxioClient.ListCustomerSubscriptionsAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { existing });

        var sut = new Microsoft.eShopWeb.ApplicationCore.Services.SubscriptionService(_mockMaxioClient, _maxioOptions);

        // Simulate a double-click: subscribe twice for the same user and plan.
        var first = await sut.SubscribeAsync(UserName, _proPlan.Handle);
        var second = await sut.SubscribeAsync(UserName, _proPlan.Handle);

        Assert.Equal(existing.Id, first.Id);
        Assert.Equal(existing.Id, second.Id);
        await _mockMaxioClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenCanceledSubscriptionToSamePlanExists_CreatesANewOne()
    {
        var customer = new MaxioCustomer { Id = 42, Reference = UserName };
        _mockMaxioClient.EnsureCustomerAsync(UserName, UserName, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(customer);
        var canceled = new MaxioSubscription { Id = 999, CustomerId = customer.Id, ProductHandle = _proPlan.Handle, State = "canceled" };
        _mockMaxioClient.ListCustomerSubscriptionsAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { canceled });
        var created = new MaxioSubscription { Id = 1002, CustomerId = customer.Id, ProductHandle = _proPlan.Handle, State = "active" };
        _mockMaxioClient.CreateSubscriptionAsync(customer.Id, _proPlan.Handle, Arg.Any<CancellationToken>())
            .Returns(created);

        var sut = new Microsoft.eShopWeb.ApplicationCore.Services.SubscriptionService(_mockMaxioClient, _maxioOptions);

        var result = await sut.SubscribeAsync(UserName, _proPlan.Handle);

        Assert.Equal(created.Id, result.Id);
    }

    [Fact]
    public async Task WhenPlanHandleUnknown_ThrowsMaxioApiException()
    {
        var sut = new Microsoft.eShopWeb.ApplicationCore.Services.SubscriptionService(_mockMaxioClient, _maxioOptions);

        await Assert.ThrowsAsync<MaxioApiException>(() => sut.SubscribeAsync(UserName, "not-a-real-plan"));

        await _mockMaxioClient.DidNotReceive().EnsureCustomerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
