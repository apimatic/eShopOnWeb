using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class GetAvailablePlans
{
    private readonly SubscriptionService _service = new(
        Substitute.For<IMaxioBillingGateway>(),
        Options.Create(new MaxioSettings()));

    [Fact]
    public async Task QueriesTheConfiguredProductFamily()
    {
        var gateway = Substitute.For<IMaxioBillingGateway>();
        var service = new SubscriptionService(
            gateway,
            Options.Create(new MaxioSettings { ProductFamilyHandle = SubscriptionServiceTestData.FAMILY_HANDLE }));

        gateway
            .GetFamilyPlansAsync(SubscriptionServiceTestData.FAMILY_HANDLE, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.Plans());

        var plans = await service.GetAvailablePlansAsync();

        Assert.Single(plans);
        Assert.Equal(SubscriptionServiceTestData.PLAN_HANDLE, plans[0].Handle);
        await gateway
            .Received(1)
            .GetFamilyPlansAsync(SubscriptionServiceTestData.FAMILY_HANDLE, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailsFast_WhenFamilyHandleIsNotConfigured()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetAvailablePlansAsync());
    }
}

public class GetSubscriptionsForUser
{
    private readonly IMaxioBillingGateway _gateway = Substitute.For<IMaxioBillingGateway>();
    private readonly SubscriptionService _service;

    public GetSubscriptionsForUser()
    {
        _service = new SubscriptionService(
            _gateway,
            Options.Create(new MaxioSettings { ProductFamilyHandle = SubscriptionServiceTestData.FAMILY_HANDLE }));
    }

    [Fact]
    public async Task ReturnsEmpty_WhenUserHasNoBillingCustomer()
    {
        _gateway.FindCustomerByReferenceAsync(SubscriptionServiceTestData.CustomerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var result = await _service.GetSubscriptionsForUserAsync(SubscriptionServiceTestData.USER_ID);

        Assert.Empty(result);
        await _gateway
            .DidNotReceive()
            .GetCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsAllOfTheUsersSubscriptions()
    {
        _gateway.FindCustomerByReferenceAsync(SubscriptionServiceTestData.CustomerReference, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.Customer());
        _gateway.GetCustomerSubscriptionsAsync(SubscriptionServiceTestData.CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(new System.Collections.Generic.List<MaxioSubscription>
            {
                SubscriptionServiceTestData.Subscription(state: "active"),
                SubscriptionServiceTestData.Subscription(state: "canceled", id: SubscriptionServiceTestData.SUBSCRIPTION_ID + 1),
            });

        var result = await _service.GetSubscriptionsForUserAsync(SubscriptionServiceTestData.USER_ID);

        Assert.Equal(2, result.Count);
    }
}
