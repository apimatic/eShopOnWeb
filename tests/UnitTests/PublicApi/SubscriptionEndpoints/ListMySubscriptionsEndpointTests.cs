using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpointTests
{
    private readonly ISubscriptionBillingService _billing = Substitute.For<ISubscriptionBillingService>();
    private readonly ListMySubscriptionsEndpoint _endpoint = new();

    [Fact]
    public async Task ReturnsUnauthorizedWhenIdentityMissing()
    {
        var result = await _endpoint.HandleAsync(new ListMySubscriptionsRequest { UserReference = "" }, _billing);

        Assert.IsType<UnauthorizedHttpResult>(result);
        await _billing.DidNotReceive().GetSubscriptionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsMappedSubscriptionsForCaller()
    {
        _billing.GetSubscriptionsAsync("user@example.com", Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription>
            {
                new() { Id = "1", State = "active", PlanHandle = "eshop-pro", PlanName = "Pro Plan", PriceInCents = 29900 }
            });

        var result = await _endpoint.HandleAsync(
            new ListMySubscriptionsRequest { UserReference = "user@example.com" }, _billing);

        var ok = Assert.IsType<Ok<ListMySubscriptionsResponse>>(result);
        Assert.Single(ok.Value!.Subscriptions);
        Assert.Equal("eshop-pro", ok.Value.Subscriptions[0].PlanHandle);
        Assert.Equal(299m, ok.Value.Subscriptions[0].Price);
    }
}
