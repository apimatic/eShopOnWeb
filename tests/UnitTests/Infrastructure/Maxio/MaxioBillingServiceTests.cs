using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingServiceTests
{
    private static readonly SubscriberIdentity Subscriber =
        SubscriberIdentity.FromUserName("demouser@microsoft.com");

    private static MaxioBillingService BuildService(HttpStatusCode status, string json)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "key", Password = "x" }
        };
        options.Server.Production.Us.BaseUrl = "https://sandbox.test";

        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "sandbox",
            ProductFamilyHandle = "eshop-subscribe"
        });

        return new MaxioBillingService(client, settings, NullLogger<MaxioBillingService>.Instance);
    }

    [Fact]
    public async Task GetSubscriptions_ReturnsEmpty_WhenShopperHasNoMaxioCustomer()
    {
        // A customer lookup that 404s is an absence, not a fault.
        var service = BuildService(HttpStatusCode.NotFound, "{}");

        var result = await service.GetSubscriptionsAsync(Subscriber);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSubscriptions_TranslatesProviderClientError_ToSameStatus()
    {
        var service = BuildService(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"boom\"]}");

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() => service.GetSubscriptionsAsync(Subscriber));

        Assert.Equal(422, ex.StatusCode);
    }

    [Fact]
    public async Task GetPlans_Fails_WhenConfiguredProductFamilyIsNotPresent()
    {
        // No product family matches the configured handle → the catalog is unavailable (upstream 5xx-class).
        var service = BuildService(HttpStatusCode.OK, "[]");

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() => service.GetPlansAsync());

        Assert.Equal(502, ex.StatusCode);
    }

    [Fact]
    public async Task Subscribe_Rejects_MissingPlanHandle_WithBadRequest()
    {
        var service = BuildService(HttpStatusCode.OK, "{}");

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() => service.SubscribeAsync(Subscriber, "   "));

        Assert.Equal(400, ex.StatusCode);
    }
}
