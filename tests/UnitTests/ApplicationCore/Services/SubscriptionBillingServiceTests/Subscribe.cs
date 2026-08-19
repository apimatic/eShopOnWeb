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
    private readonly ShopperIdentity _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");
    private readonly SubscriptionPlan _proPlan = new("eshop-pro", "Pro Plan", "Pro", 29900, 1, "month", false);

    private SubscriptionBillingService CreateService() => new(_maxio, _logger);

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new[] { _proPlan });
        _maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<CreateBillingCustomer>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(42, "user-1", _shopper.Email));
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SubscriptionDetails?)null);
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SubscriptionDetails>());
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionDetails(99, "eshop:user-1:eshop-pro", "active", 29900, "eshop-pro", "Pro Plan",
                DateTimeOffset.UtcNow.AddMonths(1), null));

        var result = await CreateService().SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.ProductHandle);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<CreateBillingCustomer>(c => c.Reference == "user-1" && c.Email == _shopper.Email),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateBillingSubscription>(s =>
                s.CustomerId == 42 &&
                s.ProductHandle == "eshop-pro" &&
                s.Reference == "eshop:user-1:eshop-pro" &&
                s.PaymentCollectionMethod == "remittance"),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotCreateSecondCustomerOrSubscriptionOnRepeatSubscribe()
    {
        var existingCustomer = new BillingCustomer(42, "user-1", _shopper.Email);
        var existingSubscription = new SubscriptionDetails(99, "eshop:user-1:eshop-pro", "active", 29900, "eshop-pro",
            "Pro Plan", DateTimeOffset.UtcNow.AddMonths(1), null);

        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new[] { _proPlan });
        _maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns(existingCustomer);
        _maxio.FindSubscriptionByReferenceAsync("eshop:user-1:eshop-pro", Arg.Any<CancellationToken>())
            .Returns(existingSubscription);

        var first = await CreateService().SubscribeAsync(_shopper, "eshop-pro");
        var second = await CreateService().SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(99, first.Id);
        Assert.Equal(99, second.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateBillingCustomer>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoversWhenCreateSubscriptionReportsConflict()
    {
        var customer = new BillingCustomer(42, "user-1", _shopper.Email);
        var existing = new SubscriptionDetails(99, "eshop:user-1:eshop-pro", "active", 29900, "eshop-pro", "Pro Plan",
            DateTimeOffset.UtcNow.AddMonths(1), null);

        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new[] { _proPlan });
        _maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns(customer);
        _maxio.FindSubscriptionByReferenceAsync("eshop:user-1:eshop-pro", Arg.Any<CancellationToken>())
            .Returns((SubscriptionDetails?)null, existing);
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SubscriptionDetails>());
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<SubscriptionDetails>(_ => throw new BillingConflictException("DuplicatePrevention::DuplicateSubmissionError"));

        var result = await CreateService().SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
    }

    [Fact]
    public async Task RecoversWhenCreateCustomerReportsConflict()
    {
        var recoveredCustomer = new BillingCustomer(42, "user-1", _shopper.Email);
        var created = new SubscriptionDetails(7, "eshop:user-1:eshop-pro", "active", 29900, "eshop-pro", "Pro Plan",
            DateTimeOffset.UtcNow.AddMonths(1), null);

        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new[] { _proPlan });
        _maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null, recoveredCustomer);
        _maxio.CreateCustomerAsync(Arg.Any<CreateBillingCustomer>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<BillingCustomer>(_ => throw new BillingConflictException("DuplicatePrevention::DuplicateSubmissionError"));
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SubscriptionDetails?)null);
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SubscriptionDetails>());
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(created);

        var result = await CreateService().SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(7, result.Id);
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateBillingSubscription>(s => s.CustomerId == 42),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsUnknownPlanHandle()
    {
        _maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new[] { _proPlan });

        await Assert.ThrowsAsync<BillingNotFoundException>(() =>
            CreateService().SubscribeAsync(_shopper, "not-a-plan"));
    }

    [Fact]
    public async Task RequiresProductHandle()
    {
        await Assert.ThrowsAsync<BillingValidationException>(() =>
            CreateService().SubscribeAsync(_shopper, "  "));
    }
}

public class ListMySubscriptions
{
    [Fact]
    public async Task ReturnsEmptyWhenCustomerDoesNotExist()
    {
        var maxio = Substitute.For<IMaxioBillingGateway>();
        var logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
        maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);

        var result = await new SubscriptionBillingService(maxio, logger)
            .ListMySubscriptionsAsync(new ShopperIdentity("user-1", "a@b.c", "a@b.c"));

        Assert.Empty(result);
        await maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
