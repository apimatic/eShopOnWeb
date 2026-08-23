using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioSubscriptionBillingServiceTests
{
    [TestMethod]
    public async Task MapsPlansFromGeneratedSdkResponses()
    {
        var handler = new QueueHttpMessageHandler(
            Json(HttpStatusCode.OK, ProductFamiliesJson),
            Json(HttpStatusCode.OK, ProductsJson));
        await using var context = NewIdentityContext();
        var service = NewService(handler, context);

        var plans = await service.GetPlansAsync(CancellationToken.None);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("eshop-pro", plans[0].Handle);
        Assert.AreEqual(29900L, plans[0].PriceInCents);
        Assert.AreEqual("month", plans[0].IntervalUnit);
        Assert.IsFalse(plans[0].RequireCreditCard);
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [DataTestMethod]
    [DataRow(true, "remittance")]
    [DataRow(false, "invoice")]
    public async Task RepeatedSubscribeCreatesOnlyOneCustomerAndSubscription(
        bool relationshipInvoicingEnabled,
        string expectedCollectionMethod)
    {
        const string userId = "user-123";
        var customerReference = StableReference("eshop-customer", userId);
        var subscriptionReference = StableReference("eshop-subscription", $"{userId}\neshop-pro");
        var customer = """
            {"customer":{"id":91001,"reference":"__CUSTOMER_REFERENCE__"}}
            """.Replace("__CUSTOMER_REFERENCE__", customerReference, StringComparison.Ordinal);
        var subscription = """
            {"subscription":{"id":82001,"reference":"__SUBSCRIPTION_REFERENCE__","state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-09-24T00:00:00Z","product":{"id":7126957,"handle":"eshop-pro","name":"eShop Pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}
            """.Replace("__SUBSCRIPTION_REFERENCE__", subscriptionReference, StringComparison.Ordinal);

        var handler = new QueueHttpMessageHandler(
            Json(HttpStatusCode.OK, ProductJson),
            Json(HttpStatusCode.NotFound, "{}"),
            Json(HttpStatusCode.Created, customer),
            Json(HttpStatusCode.NotFound, "{}"),
            Json(HttpStatusCode.OK, "[]"),
            Json(HttpStatusCode.OK,
                relationshipInvoicingEnabled ? RelationshipInvoicingSiteJson : LegacySiteJson),
            Json(HttpStatusCode.Created, subscription),
            Json(HttpStatusCode.OK, ProductJson),
            Json(HttpStatusCode.OK, customer),
            Json(HttpStatusCode.OK, subscription));
        await using var context = NewIdentityContext();
        var service = NewService(handler, context);
        var user = new BillingUser(userId, "shopper@example.com", "shopper", "eShop");

        var first = await service.SubscribeAsync(user, "eshop-pro", CancellationToken.None);
        var second = await service.SubscribeAsync(user, "eshop-pro", CancellationToken.None);

        Assert.IsTrue(first.Created);
        Assert.IsFalse(second.Created);
        Assert.AreEqual(first.Subscription.Id, second.Subscription.Id);
        Assert.AreEqual(2, handler.Requests.Count(request => request.Method == HttpMethod.Post));
        Assert.AreEqual(1, handler.Bodies.Count(body => body.Contains("\"product_handle\":\"eshop-pro\"", StringComparison.Ordinal)));
        Assert.AreEqual(1, handler.Requests.Count(request =>
            request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/site.json"));
        Assert.AreEqual(1, handler.Bodies.Count(body => body.Contains(
            $"\"payment_collection_method\":\"{expectedCollectionMethod}\"",
            StringComparison.Ordinal)));
        Assert.AreEqual(1, await context.MaxioSubscriptionClaims.CountAsync());
        Assert.AreEqual(MaxioSubscriptionClaimStatus.Active,
            (await context.MaxioSubscriptionClaims.SingleAsync()).Status);
    }

    [TestMethod]
    public async Task WriteGuardBlocksSdkTransportRetryBeforeSecondNetworkSend()
    {
        var terminalHandler = new ThrowingHttpMessageHandler();
        var callContext = new MaxioHttpCallContext();
        var guard = new MaxioHttpHandler(callContext) { InnerHandler = terminalHandler };
        var options = NewClientOptions();
        options.Retry = RetryOptions.Default() with
        {
            MaxRetries = 1,
            Delay = TimeSpan.Zero,
            MaxJitter = TimeSpan.Zero
        };
        var client = new MaxioAdvancedBilling.MaxioAdvancedBillingClient(new HttpClient(guard), options);

        using var scope = callContext.Begin(writeOnce: true);
        await Assert.ThrowsExceptionAsync<MaxioWriteResendBlockedException>(() =>
            client.Subscriptions.CreateSubscription(
                body: new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = "eshop-pro",
                        CustomerReference = "customer-reference",
                        Reference = "subscription-reference"
                    }
                },
                ct: CancellationToken.None));

        Assert.AreEqual(1, terminalHandler.SendCount);
    }

    private static MaxioSubscriptionBillingService NewService(
        HttpMessageHandler terminalHandler,
        AppIdentityDbContext context)
    {
        var callContext = new MaxioHttpCallContext();
        var guard = new MaxioHttpHandler(callContext) { InnerHandler = terminalHandler };
        var options = NewClientOptions();
        var client = new MaxioAdvancedBilling.MaxioAdvancedBillingClient(new HttpClient(guard), options);

        return new MaxioSubscriptionBillingService(
            client,
            context,
            Options.Create(new MaxioOptions
            {
                ApiKey = "test-key",
                Subdomain = "test-site",
                ProductFamilyHandle = "eshop-subscribe"
            }),
            callContext,
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions NewClientOptions()
    {
        var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
        };
        options.Server.Production.Us.BaseUrl = "https://maxio.test";
        return options;
    }

    private static AppIdentityDbContext NewIdentityContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppIdentityDbContext(options);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
        => new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string StableReference(string prefix, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"{prefix}-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private const string ProductFamiliesJson = """
        [{"product_family":{"id":3023074,"handle":"eshop-subscribe"}}]
        """;

    private const string ProductsJson = """
        [{"product":{"id":7126957,"handle":"eshop-pro","name":"eShop Pro","price_in_cents":29900,"interval":1,"interval_unit":"month","request_credit_card":false,"require_credit_card":false,"archived_at":null,"product_family":{"id":3023074,"handle":"eshop-subscribe"}}}]
        """;

    private const string ProductJson = """
        {"product":{"id":7126957,"handle":"eshop-pro","name":"eShop Pro","price_in_cents":29900,"interval":1,"interval_unit":"month","request_credit_card":false,"require_credit_card":false,"archived_at":null,"product_family":{"id":3023074,"handle":"eshop-subscribe"}}}
        """;

    private const string RelationshipInvoicingSiteJson = """
        {"site":{"relationship_invoicing_enabled":true}}
        """;

    private const string LegacySiteJson = """
        {"site":{"relationship_invoicing_enabled":false}}
        """;

    private sealed class QueueHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("The test did not configure another Maxio response.");
            }

            return _responses.Dequeue();
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            throw new HttpRequestException("Simulated connection reset.");
        }
    }
}
