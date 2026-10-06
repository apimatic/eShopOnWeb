using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class GetMySubscriptions
{
    [Fact]
    public async Task ReturnsEmptyWhenTheUserHasNoMaxioCustomerYet()
    {
        var harness = new SubscriptionServiceHarness();
        harness.Maxio.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var result = await harness.CreateService().GetSubscriptionsForUserAsync(SubscriptionServiceHarness.USER_ID);

        Assert.Empty(result);
        await harness.Maxio.DidNotReceiveWithAnyArgs()
            .GetSubscriptionsForCustomerAsync(default, default);
    }

    [Fact]
    public async Task ReadsTheProvidersLedgerAndMirrorsItForTheUser()
    {
        var harness = new SubscriptionServiceHarness();
        harness.Maxio.FindCustomerByReferenceAsync($"eshoponweb-user:{SubscriptionServiceHarness.USER_ID}", Arg.Any<CancellationToken>())
            .Returns(harness.Customer);
        harness.Maxio.GetSubscriptionsForCustomerAsync(harness.Customer.CustomerId, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                harness.LiveSubscription(id: 1),
                harness.LiveSubscription(id: 2, reference: "eshoponweb-sub:demouser@microsoft.com:basic-plan") with
                {
                    PlanHandle = "basic-plan",
                    PlanName = "Basic Plan",
                    PriceInCents = 2900
                }
            });

        var result = await harness.CreateService().GetSubscriptionsForUserAsync(SubscriptionServiceHarness.USER_ID);

        Assert.Equal(2, result.Count);
        Assert.Equal(new long[] { 2, 1 }, result.Select(s => s.MaxioSubscriptionId).ToArray());
        Assert.Equal(2, harness.Store.Count);
    }
}
