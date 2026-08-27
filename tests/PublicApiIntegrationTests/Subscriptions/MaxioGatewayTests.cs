using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class MaxioGatewayTests
{
    [TestMethod]
    public async Task ListsPlansByConfiguredFamilyHandleAndRuntimeId()
    {
        var handler = new StubHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/product_families.json" => Json(HttpStatusCode.OK,
                """[{"product_family":{"id":42,"handle":"configured-family"}}]"""),
            "/product_families/42/products.json" => Json(HttpStatusCode.OK,
                """[{"product":{"name":"Pro","handle":"eshop-pro","description":"Pro plan","price_in_cents":29900,"interval":1,"interval_unit":"month","product_family":{"handle":"configured-family"}}}]"""),
            _ => Json(HttpStatusCode.NotFound, "{}")
        });
        var gateway = CreateGateway(handler);

        var plans = await gateway.ListPlansAsync(CancellationToken.None);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("eshop-pro", plans[0].Handle);
        Assert.AreEqual(29900L, plans[0].PriceInCents);
        Assert.AreEqual("month", plans[0].IntervalUnit);
        var productRequest = handler.Requests.Single(request => request.Path.Contains("/products.json", StringComparison.Ordinal));
        StringAssert.Contains(productRequest.Query, "page=1");
        StringAssert.Contains(productRequest.Query, "per_page=100");
        StringAssert.Contains(productRequest.Query, "include_archived=false");
    }

    [TestMethod]
    public async Task CreatesSubscriptionWithHandlesAndStableReferences()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK,
            """{"subscription":{"id":91,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-09-27T00:00:00Z","reference":"stable-subscription","product":{"handle":"eshop-pro","name":"Pro","product_family":{"handle":"configured-family"}}}}"""));
        var gateway = CreateGateway(handler);

        var result = await gateway.CreateSubscriptionAsync(
            "stable-user",
            "eshop-pro",
            "stable-subscription",
            CancellationToken.None);

        Assert.AreEqual(91, result.Id);
        Assert.AreEqual("active", result.State);
        Assert.AreEqual(29900L, result.PriceInCents);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero), result.NextBillingDate);
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        Assert.AreEqual("/subscriptions.json", handler.Requests[0].Path);
        StringAssert.Contains(handler.Requests[0].Body!, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(handler.Requests[0].Body!, "\"payment_collection_method\":\"remittance\"");
        StringAssert.Contains(handler.Requests[0].Body!, "\"customer_reference\":\"stable-user\"");
        StringAssert.Contains(handler.Requests[0].Body!, "\"reference\":\"stable-subscription\"");
    }

    [TestMethod]
    public async Task TransportRetryNeverSendsSubscriptionPostTwice()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("simulated reset"));
        var gateway = CreateGateway(handler);

        await Assert.ThrowsExceptionAsync<MaxioAmbiguousWriteException>(() =>
            gateway.CreateSubscriptionAsync("stable-user", "eshop-pro", "stable-subscription", CancellationToken.None));

        Assert.AreEqual(1, handler.Requests.Count(request => request.Method == HttpMethod.Post));
    }

    private static MaxioGateway CreateGateway(StubHandler terminalHandler)
    {
        var scope = new MaxioWriteOnceScope();
        var guard = new MaxioWriteOnceHandler(scope) { InnerHandler = terminalHandler };
        var httpClient = new HttpClient(guard) { Timeout = TimeSpan.FromSeconds(2) };
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "not-a-secret", Password = "x" },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(1)
            }
        };
        clientOptions.Server.Production.Us.BaseUrl = "https://maxio.example.test";
        var sdkClient = new MaxioAdvancedBillingClient(httpClient, clientOptions);
        return new MaxioGateway(
            sdkClient,
            Options.Create(new MaxioOptions
            {
                ApiKey = "not-a-secret",
                Subdomain = "unused",
                ProductFamilyHandle = "configured-family",
                BaseUrl = "https://maxio.example.test"
            }),
            new MemoryCache(new MemoryCacheOptions()),
            scope);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private int _attempt;
        public List<RequestSnapshot> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RequestSnapshot(request.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, body));
            return responder(request, Interlocked.Increment(ref _attempt));
        }
    }

    private sealed record RequestSnapshot(HttpMethod Method, string Path, string Query, string? Body);
}
