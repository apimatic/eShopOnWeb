using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services.MaxioSubscriptionServiceTests;

public class GetCustomerSubscriptionsAsync
{
    private static MaxioSubscriptionService CreateService(StubMaxioHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
        };
        options.Server.Production.Us.Site = "test-site";

        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = Options.Create(new MaxioSettings { ProductFamilyHandle = "test-family" });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Substitute.For<ILogger<MaxioSubscriptionService>>();

        return new MaxioSubscriptionService(client, settings, cache, logger);
    }

    [Fact]
    public async Task ReturnsEmptyListWhenNoBillingCustomerExistsYet()
    {
        var handler = new StubMaxioHandler(new (HttpStatusCode, string)[]
        {
            (HttpStatusCode.NotFound, """{"errors":["Customer not found"]}"""),
        });

        var service = CreateService(handler);

        var result = await service.GetCustomerSubscriptionsAsync("brand-new-user");

        Assert.Empty(result);
        Assert.Single(handler.Requests);
    }
}
