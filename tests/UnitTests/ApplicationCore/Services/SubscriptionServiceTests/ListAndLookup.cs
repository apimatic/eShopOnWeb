using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class ListAndLookup
{
    private readonly ISubscriptionBillingClient _mockBilling = Substitute.For<ISubscriptionBillingClient>();
    private readonly IAppLogger<SubscriptionService> _mockLogger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly SubscriptionService _service;

    public ListAndLookup()
    {
        _service = new SubscriptionService(_mockBilling, _mockLogger);
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsCatalogPlans()
    {
        var plans = new List<BillingPlan>
        {
            new() { Id = 1, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900 },
            new() { Id = 2, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 },
        };
        _mockBilling.GetPlansAsync(default).Returns(plans);

        var result = await _service.ListPlansAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("eshop-pro", result[1].Handle);
    }

    [Fact]
    public async Task GetSubscriptionsForUserAsync_ReturnsEmpty_WhenShopperHasNoBillingCustomer()
    {
        _mockBilling.FindCustomerByReferenceAsync("nobody@example.com", default).Returns((BillingCustomer?)null);

        var result = await _service.GetSubscriptionsForUserAsync("nobody@example.com");

        Assert.Empty(result);
        await _mockBilling.DidNotReceive().GetSubscriptionsForCustomerAsync(Arg.Any<int>(), default);
    }

    [Fact]
    public async Task GetSubscriptionsForUserAsync_ReturnsCustomerSubscriptions()
    {
        var customer = new BillingCustomer { Id = 7, Email = "someone@example.com" };
        _mockBilling.FindCustomerByReferenceAsync("someone@example.com", default).Returns(customer);
        _mockBilling.GetSubscriptionsForCustomerAsync(7, default).Returns(new List<BillingSubscription>
        {
            new() { Id = 100, State = "active", PlanHandle = "eshop-pro", CustomerId = 7 },
            new() { Id = 101, State = "canceled", PlanHandle = "basic-plan", CustomerId = 7 },
        });

        var result = await _service.GetSubscriptionsForUserAsync("someone@example.com");

        Assert.Equal(2, result.Count);
        Assert.Equal(100, result[0].Id);
        Assert.Equal(101, result[1].Id);
    }
}
