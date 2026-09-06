using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class ListSubscriptionsAsync : SubscriptionServiceTestBase
{
    [Fact]
    public async Task ReturnsNothingAndCreatesNoCustomerForAShopperWhoNeverSubscribed()
    {
        BillingGateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        var subscriptions = await CreateService().ListSubscriptionsAsync(UserName);

        Assert.Empty(subscriptions);
        await BillingGateway.DidNotReceive().CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsTheShoppersSubscriptionsNewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        BillingGateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        BillingGateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                Subscription(1, BasicPlanHandle, "canceled", now.AddDays(-30)),
                Subscription(2, ProPlanHandle, "active", now)
            });

        var subscriptions = await CreateService().ListSubscriptionsAsync(UserName);

        Assert.Equal(new[] { 2, 1 }, subscriptions.Select(subscription => subscription.Id).ToArray());
    }
}
