using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace PublicApiIntegrationTests.Maxio;

[TestClass]
public class MaxioSubscriptionServiceTests
{
    [TestMethod]
    public async Task ListSubscriptionsReturnsEmptyWhenNoCustomerExists()
    {
        var service = CreateService(new FixedStatusHandler(HttpStatusCode.NotFound, "Not Found"));

        var result = await service.ListSubscriptionsAsync("demouser@microsoft.com", CancellationToken.None);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task SubscribeThrowsPlanNotFoundWhenProductUnknown()
    {
        var service = CreateService(new FixedStatusHandler(HttpStatusCode.NotFound, "Not Found"));
        var profile = new MaxioCustomerProfile
        {
            UserName = "demouser@microsoft.com",
            Email = "demouser@microsoft.com",
            FirstName = "demouser",
            LastName = "microsoft"
        };

        await Assert.ThrowsExceptionAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(profile, "eshop-pro", CancellationToken.None));
    }

    private static MaxioSubscriptionService CreateService(HttpMessageHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(
            new HttpClient(handler),
            new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = "dummy-key",
                    Password = "x"
                }
            });

        var options = new MaxioOptions
        {
            ApiKey = "dummy-key",
            Subdomain = "cp-exp-5",
            ProductFamilyHandle = "eshop-subscribe"
        };

        return new MaxioSubscriptionService(client, options, NullLogger<MaxioSubscriptionService>.Instance);
    }
}
