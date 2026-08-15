using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpointTests
{
    private readonly ISubscriptionBillingService _billing = Substitute.For<ISubscriptionBillingService>();
    private readonly CreateSubscriptionEndpoint _endpoint = new();

    [Fact]
    public async Task ReturnsBadRequestWhenPlanHandleMissing()
    {
        var request = new CreateSubscriptionRequest { UserReference = "user@example.com", PlanHandle = "" };

        var result = await _endpoint.HandleAsync(request, _billing);

        Assert.IsType<BadRequest<string>>(result);
        await _billing.DidNotReceive().SubscribeAsync(Arg.Any<SubscribeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsUnauthorizedWhenIdentityMissing()
    {
        var request = new CreateSubscriptionRequest { UserReference = "", PlanHandle = "eshop-pro" };

        var result = await _endpoint.HandleAsync(request, _billing);

        Assert.IsType<UnauthorizedHttpResult>(result);
        await _billing.DidNotReceive().SubscribeAsync(Arg.Any<SubscribeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribesUsingIdentityFromRequestAndMapsResponse()
    {
        var request = new CreateSubscriptionRequest { UserReference = "user@example.com", PlanHandle = "eshop-pro" };
        _billing.SubscribeAsync(Arg.Any<SubscribeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CustomerSubscription
            {
                Id = "123",
                State = "active",
                PlanHandle = "eshop-pro",
                PlanName = "Pro Plan",
                PriceInCents = 29900
            });

        var result = await _endpoint.HandleAsync(request, _billing);

        var ok = Assert.IsType<Ok<CreateSubscriptionResponse>>(result);
        Assert.NotNull(ok.Value!.Subscription);
        Assert.Equal("123", ok.Value.Subscription!.Id);
        Assert.Equal("active", ok.Value.Subscription.State);
        Assert.Equal(299m, ok.Value.Subscription.Price);

        await _billing.Received(1).SubscribeAsync(
            Arg.Is<SubscribeRequest>(r => r.UserReference == "user@example.com"
                && r.PlanHandle == "eshop-pro"
                && r.Email == "user@example.com"),
            Arg.Any<CancellationToken>());
    }
}
