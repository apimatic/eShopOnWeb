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
public class MaxioBillingGatewayTests
{
    [TestMethod]
    public async Task ListsEligiblePlansFromConfiguredFamilyAndFiltersCardRequiredProducts()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/product_families.json", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    "[{\"product_family\":{\"id\":3023074,\"handle\":\"eshop-subscribe\",\"name\":\"Subscriptions\"}}]");
            }

            return Json(HttpStatusCode.OK,
                "[" +
                "{\"product\":{\"name\":\"Pro\",\"handle\":\"eshop-pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false,\"product_price_point_handle\":\"pro-default\"}}," +
                "{\"product\":{\"name\":\"Card Plan\",\"handle\":\"card-plan\",\"price_in_cents\":1000,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":true}}" +
                "]");
        });
        var gateway = CreateGateway(new HttpClient(handler));

        var plans = await gateway.ListPlansAsync(CancellationToken.None);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("eshop-pro", plans[0].Handle);
        Assert.AreEqual(29900L, plans[0].PriceInCents);
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsTrue(handler.Requests[1].RequestUri!.Query.Contains("per_page=100", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CreatesNoCardSubscriptionUsingRemittanceCollection()
    {
        string? requestBody = null;
        var handler = new StubHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.Created,
                "{\"subscription\":{\"id\":93930743,\"reference\":\"subscription-reference\",\"state\":\"active\",\"product_price_in_cents\":29900,\"currency\":\"USD\",\"next_assessment_at\":\"2026-09-21T00:00:00Z\",\"product\":{\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\",\"product_family\":{\"handle\":\"eshop-subscribe\"}}}}");
        });
        var gateway = CreateGateway(new HttpClient(handler));

        var subscription = await gateway.CreateSubscriptionAsync(
            new MaxioCustomer(123, "customer-reference"),
            new MaxioProduct("eshop-pro", "Pro Plan", null, 29900, 1, "month", "pro-default"),
            "subscription-reference",
            CancellationToken.None);

        Assert.AreEqual(93930743, subscription.Id);
        StringAssert.Contains(requestBody, "\"payment_collection_method\":\"remittance\"");
        Assert.IsFalse(requestBody!.Contains("credit_card", StringComparison.Ordinal));
        Assert.IsFalse(requestBody.Contains("payment_profile", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task BlocksSdkTransportRetryForSubscriptionPost()
    {
        var terminal = new ThrowingHandler();
        var writeGuard = new MaxioWriteOnceHandler { InnerHandler = terminal };
        var gateway = CreateGateway(new HttpClient(writeGuard));

        await Assert.ThrowsExceptionAsync<MaxioWriteOutcomeUnknownException>(() =>
            gateway.CreateSubscriptionAsync(
                new MaxioCustomer(123, "customer-reference"),
                new MaxioProduct("eshop-pro", "Pro", null, 29900, 1, "month", "pro-default"),
                "subscription-reference",
                CancellationToken.None));

        Assert.AreEqual(1, terminal.SendCount);
    }

    private static MaxioBillingGateway CreateGateway(HttpClient httpClient)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
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
        clientOptions.Server.Production.Us.BaseUrl = "https://maxio.test";
        var sdkClient = new MaxioAdvancedBillingClient(httpClient, clientOptions);
        var settings = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            ProductFamilyHandle = "eshop-subscribe",
            BaseUrl = "https://maxio.test"
        });
        return new MaxioBillingGateway(sdkClient, settings, NullLogger<MaxioBillingGateway>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            throw new HttpRequestException("simulated connection reset");
        }
    }
}
