using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private readonly IMaxioAdvancedBillingGateway _maxio = Substitute.For<IMaxioAdvancedBillingGateway>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly ShopperIdentity _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");
    private readonly SubscriptionPlan _pro = new()
    {
        Id = 1,
        Handle = "eshop-pro",
        Name = "Pro Plan",
        Price = 299m,
        Interval = 1,
        IntervalUnit = "month"
    };

    private SubscriptionBillingService CreateService() => new(_maxio, _logger);

    [Fact]
    public async Task CreatesCustomerThenSubscriptionWhenShopperIsNew()
    {
        _maxio.ListConfiguredFamilyProductsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan> { _pro });
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _maxio.CreateCustomerAsync(_shopper.UserId, Arg.Any<string>(), Arg.Any<string>(), _shopper.Email, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = _shopper.UserId, Email = _shopper.Email });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CustomerSubscription
            {
                Id = 99,
                State = "active",
                PlanHandle = "eshop-pro",
                PlanName = "Pro Plan",
                Price = 299m,
                NextBillingAt = DateTimeOffset.UtcNow.AddMonths(1)
            });

        var result = await CreateService().SubscribeAsync(_shopper, "eshop-pro", default);

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        await _maxio.Received(1).CreateCustomerAsync(_shopper.UserId, "Demouser", "Customer", _shopper.Email, Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(42, "eshop-pro", "eshop:user-1:eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReusesLiveSubscriptionInsteadOfCreatingAnother()
    {
        _maxio.ListConfiguredFamilyProductsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan> { _pro });
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = _shopper.UserId, Email = _shopper.Email });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription>
            {
                new()
                {
                    Id = 99,
                    State = "active",
                    PlanHandle = "eshop-pro",
                    PlanName = "Pro Plan",
                    Price = 299m
                }
            });

        var result = await CreateService().SubscribeAsync(_shopper, "eshop-pro", default);

        Assert.Equal(99, result.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoversWhenCreateSubscriptionConflicts()
    {
        _maxio.ListConfiguredFamilyProductsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan> { _pro });
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = _shopper.UserId, Email = _shopper.Email });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(
                Array.Empty<CustomerSubscription>(),
                new List<CustomerSubscription>
                {
                    new()
                    {
                        Id = 99,
                        State = "active",
                        PlanHandle = "eshop-pro",
                        PlanName = "Pro Plan",
                        Price = 299m
                    }
                });
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<CustomerSubscription>(_ => throw new MaxioApiException("duplicate", 409));

        var result = await CreateService().SubscribeAsync(_shopper, "eshop-pro", default);

        Assert.Equal(99, result.Id);
    }

    [Fact]
    public async Task RejectsUnknownProductHandle()
    {
        _maxio.ListConfiguredFamilyProductsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan> { _pro });

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => CreateService().SubscribeAsync(_shopper, "not-a-plan", default));

        Assert.Equal((int)HttpStatusCode.NotFound, ex.StatusCode);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequiresProductHandle()
    {
        await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => CreateService().SubscribeAsync(_shopper, "  ", default));
    }

    [Fact]
    public async Task ListMySubscriptionsReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var result = await CreateService().ListMySubscriptionsAsync(_shopper, default);

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("demouser@microsoft.com", "demouser@microsoft.com", "Demouser", "Customer")]
    [InlineData("ada.lovelace@example.com", "ada.lovelace@example.com", "Ada", "Lovelace")]
    public void SplitsShopperNameFromEmail(string email, string userName, string expectedFirst, string expectedLast)
    {
        var (first, last) = SubscriptionBillingService.SplitDisplayName(new ShopperIdentity("id", email, userName));
        Assert.Equal(expectedFirst, first);
        Assert.Equal(expectedLast, last);
    }
}
