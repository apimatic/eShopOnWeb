using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Maxio.Configuration;
using Microsoft.eShopWeb.Maxio.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class ListPlansTests
{
    private static MaxioOptions CreateOptions() => new()
    {
        ApiKey = "test-api-key",
        Subdomain = "test",
        ProductFamilyHandle = "eshop-subscribe",
        Environment = "US"
    };

    [Fact]
    public async Task ListsOnlyActivePlansOfConfiguredFamilyWithSiteCurrency()
    {
        var client = new FakeMaxioApiClient();
        var service = new MaxioBillingService(client, CreateOptions());

        var plans = await service.GetPlansAsync(CancellationToken.None);

        Assert.Equal(2, plans.Count);
        var pro = Assert.Single(plans, p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal("USD", pro.Currency);
        Assert.False(pro.RequiresCreditCard);
        Assert.DoesNotContain(plans, p => p.Handle == "retired-plan");
    }
}
