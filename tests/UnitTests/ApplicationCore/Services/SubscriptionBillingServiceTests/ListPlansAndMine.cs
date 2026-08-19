using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class ListPlansAndMine
{
    private readonly IAdvancedBillingGateway _gateway = Substitute.For<IAdvancedBillingGateway>();
    private readonly SubscriptionBillingService _service;
    private readonly BillingShopper _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");

    public ListPlansAndMine()
    {
        _service = new SubscriptionBillingService(
            _gateway,
            new UserKeyedLock(),
            Substitute.For<IAppLogger<SubscriptionBillingService>>());
    }

    [Fact]
    public async Task ListPlansMapsPriceFromCents()
    {
        _gateway.ListCatalogPlansAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new BillingProduct
            {
                Id = 1,
                Handle = "eshop-pro",
                Name = "Pro Plan",
                Description = "Full access",
                PriceInCents = 29900,
                Interval = 1,
                IntervalUnit = "month",
                RequireCreditCard = false
            }
        });

        var plans = await _service.ListPlansAsync(CancellationToken.None);

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(plan.RequireCreditCard);
    }

    [Fact]
    public async Task GetSubscriptionsReturnsEmptyWhenCustomerDoesNotExist()
    {
        _gateway.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        var subscriptions = await _service.GetSubscriptionsAsync(_shopper, CancellationToken.None);

        Assert.Empty(subscriptions);
        await _gateway.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
