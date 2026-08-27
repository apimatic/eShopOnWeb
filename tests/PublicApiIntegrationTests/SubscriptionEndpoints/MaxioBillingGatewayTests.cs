using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioBillingGatewayTests
{
    [TestMethod]
    public async Task PlansAreProjectedThroughTheSdkHttpSeam()
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            Json(HttpStatusCode.OK,
                """[{"product_family":{"id":3023074,"name":"eShop","handle":"eshop-subscribe"}}]"""),
            Json(HttpStatusCode.OK,
                """[{"product":{"id":7126957,"name":"Pro","handle":"eshop-pro","description":"Pro plan","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null}}]""")
        });
        var handler = new RecordingHandler(_ => responses.Dequeue());
        var gateway = CreateGateway(handler);

        var plans = await gateway.GetPlansAsync(CancellationToken.None);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("eshop-pro", plans[0].Handle);
        Assert.AreEqual(29900L, plans[0].PriceInCents);
        Assert.AreEqual("month", plans[0].IntervalUnit);
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsTrue(handler.Requests.TrueForAll(request => request.Method == HttpMethod.Get));
    }

    [TestMethod]
    public async Task SubscriptionTransportRetryCannotSendASecondPost()
    {
        var network = new RecordingHandler(_ => throw new HttpRequestException("simulated reset"));
        var context = new MaxioHttpCallContext();
        var guard = new MaxioHttpPipelineHandler(context) { InnerHandler = network };
        var gateway = CreateGateway(guard, context);

        var exception = await Assert.ThrowsExceptionAsync<MaxioProviderException>(() =>
            gateway.CreateSubscriptionAsync("eshop-pro", 123, "stable-reference", CancellationToken.None));

        Assert.IsTrue(exception.OutcomeUnknown);
        Assert.AreEqual(1, network.Requests.Count);
        Assert.AreEqual(HttpMethod.Post, network.Requests[0].Method);
    }

    private static MaxioBillingGateway CreateGateway(HttpMessageHandler handler,
        MaxioHttpCallContext? context = null)
    {
        context ??= new MaxioHttpCallContext();
        var sdkOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(1)
            },
            BasicAuth = new BasicAuthCredentials { Username = "test", Password = "x" }
        };
        sdkOptions.Server.Production.Us.BaseUrl = "https://maxio.invalid";
        var sdk = new MaxioAdvancedBillingClient(new HttpClient(handler), sdkOptions);
        var appOptions = Options.Create(new MaxioOptions
        {
            ApiKey = "test",
            Subdomain = "unused",
            ProductFamilyHandle = "eshop-subscribe",
            BaseUrl = "https://maxio.invalid"
        });
        return new MaxioBillingGateway(sdk, appOptions, context,
            NullLogger<MaxioBillingGateway>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }
}
