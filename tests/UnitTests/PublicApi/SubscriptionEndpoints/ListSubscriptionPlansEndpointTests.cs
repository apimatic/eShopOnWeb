using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpointTests
{
    private readonly ISubscriptionBillingService _billing = Substitute.For<ISubscriptionBillingService>();
    private readonly ListSubscriptionPlansEndpoint _endpoint = new();

    [Fact]
    public async Task ReturnsMappedPlans()
    {
        _billing.GetPlansAsync(Arg.Any<CancellationToken>()).Returns(new List<SubscriptionPlan>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
            new() { Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" }
        });

        var result = await _endpoint.HandleAsync(new ListSubscriptionPlansRequest(), _billing);

        var ok = Assert.IsType<Ok<ListSubscriptionPlansResponse>>(result);
        Assert.Equal(2, ok.Value!.Plans.Count);
        var pro = ok.Value.Plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal(299m, pro.Price);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);
    }
}
