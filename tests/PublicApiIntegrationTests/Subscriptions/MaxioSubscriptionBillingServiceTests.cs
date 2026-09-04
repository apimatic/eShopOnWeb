using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class MaxioSubscriptionBillingServiceTests
{
    [TestMethod]
    public async Task DiscoversConfiguredFamilyAndMapsPlan()
    {
        var handler = new StubHandler((_, requestNumber) => requestNumber == 1
            ? Json(HttpStatusCode.OK, "[{\"product_family\":{\"id\":42,\"handle\":\"test-family\",\"name\":\"Test family\"}}]")
            : Json(HttpStatusCode.OK, "[{\"product\":{\"handle\":\"test-plan\",\"name\":\"Test plan\",\"price_in_cents\":1234,\"interval\":1,\"interval_unit\":\"month\"}}]"));
        var client = CreateClient(handler);
        var service = new MaxioSubscriptionBillingService(
            client,
            Options.Create(new MaxioOptions { ApiKey = "key", Subdomain = "site", ProductFamilyHandle = "test-family" }),
            new ConcurrentDictionary<string, SemaphoreSlim>(),
            NullLogger<MaxioSubscriptionBillingService>.Instance);

        var plans = await service.GetPlansAsync(CancellationToken.None);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("test-plan", plans[0].PlanHandle);
        Assert.AreEqual(1234, plans[0].PriceInCents);
        Assert.AreEqual("month", plans[0].IntervalUnit);
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
    }

    private static MaxioAdvancedBillingClient CreateClient(StubHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "key", Password = "x" }
        };
        options.Server.Production.Us.Site = "site";
        return new MaxioAdvancedBillingClient(new HttpClient(handler), options);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public System.Collections.Generic.List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request, Requests.Count));
        }
    }
}
