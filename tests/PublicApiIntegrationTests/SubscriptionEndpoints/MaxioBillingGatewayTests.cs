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
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public sealed class MaxioBillingGatewayTests
{
    [TestMethod]
    public async Task ListsPlansByResolvingTheConfiguredFamilyIdAtRuntime()
    {
        var handler = new StubHandler((request, attempt) => attempt switch
        {
            1 => Json(HttpStatusCode.OK,
                """[{"product_family":{"id":3023074,"name":"eShop","handle":"configured-family","archived_at":null}}]"""),
            2 => Json(HttpStatusCode.OK,
                """[{"product":{"name":"Pro","handle":"eshop-pro","description":"Pro plan","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false}}]"""),
            _ => throw new AssertFailedException("Unexpected Maxio request")
        });
        var gateway = CreateGateway(handler);

        var plans = await gateway.ListPlansAsync(CancellationToken.None);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("eshop-pro", plans[0].Handle);
        Assert.AreEqual(29900L, plans[0].PriceInCents);
        Assert.AreEqual("month", plans[0].IntervalUnit);
        Assert.IsFalse(plans[0].RequiresPaymentMethod);
        Assert.AreEqual(2, handler.Requests.Count);
        StringAssert.Contains(handler.Requests[1].Uri, "3023074");
        StringAssert.Contains(handler.Requests[1].Uri, "include_archived=false");
    }

    [TestMethod]
    public async Task CreatesSubscriptionWithRemittanceAndNoPaymentPayload()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK,
            """{"subscription":{"id":202,"state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-09-25T00:00:00Z","next_assessment_at":"2026-09-25T00:00:00Z","reference":"stable-subscription-reference","product":{"name":"Pro","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}"""));
        var gateway = CreateGateway(handler);

        var subscription = await gateway.CreateSubscriptionAsync(
            "stable-customer-reference",
            "eshop-pro",
            "stable-subscription-reference",
            CancellationToken.None);

        Assert.AreEqual(202, subscription.MaxioSubscriptionId);
        Assert.AreEqual("active", subscription.State);
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        StringAssert.Contains(handler.Requests[0].Body, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(handler.Requests[0].Body, "\"customer_reference\":\"stable-customer-reference\"");
        StringAssert.Contains(handler.Requests[0].Body, "\"reference\":\"stable-subscription-reference\"");
        StringAssert.Contains(handler.Requests[0].Body, "\"payment_collection_method\":\"remittance\"");
        Assert.IsFalse(handler.Requests[0].Body.Contains("credit_card", StringComparison.Ordinal));
        Assert.IsFalse(handler.Requests[0].Body.Contains("payment_profile", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task TransportRetryCannotSendTheSubscriptionPostTwice()
    {
        var handler = new StubHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post) throw new HttpRequestException("connection reset");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var gateway = CreateGateway(handler);

        var exception = await Assert.ThrowsExceptionAsync<MaxioProviderException>(() =>
            gateway.CreateSubscriptionAsync(
                "stable-customer-reference",
                "eshop-pro",
                "stable-subscription-reference",
                CancellationToken.None));

        Assert.AreEqual(MaxioFailureKind.AmbiguousWrite, exception.Kind);
        Assert.AreEqual(1, handler.PostAttempts);
    }

    private static MaxioBillingGateway CreateGateway(StubHandler stub)
    {
        var context = new MaxioRequestContext();
        var outer = new MaxioHttpHandler(context, NullLogger<MaxioHttpHandler>.Instance)
        {
            InnerHandler = stub
        };
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Delay = TimeSpan.Zero,
                MaxJitter = TimeSpan.Zero,
                Timeout = TimeSpan.FromSeconds(2)
            }
        };
        options.Server.Production.Us.BaseUrl = "https://maxio.test";
        var client = new MaxioAdvancedBillingClient(new HttpClient(outer), options);
        return new MaxioBillingGateway(
            client,
            Options.Create(new MaxioOptions
            {
                ApiKey = "test-key",
                Subdomain = "test-site",
                ProductFamilyHandle = "configured-family",
                BaseUrl = "https://maxio.test"
            }),
            context);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responder;
        private int _attempts;

        public StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder) => _responder = responder;

        public List<CapturedRequest> Requests { get; } = new();
        public int PostAttempts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!.ToString(), body));
            if (request.Method == HttpMethod.Post) PostAttempts++;
            return _responder(request, Interlocked.Increment(ref _attempts));
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, string Uri, string Body);
}
