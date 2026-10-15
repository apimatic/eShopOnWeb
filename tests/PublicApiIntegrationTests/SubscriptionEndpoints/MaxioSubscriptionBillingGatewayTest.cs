using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioSubscriptionBillingGatewayTest
{
    private const string SubscriptionJson =
        """{ "subscription": { "id": 501, "state": "active", "reference": "eshop-sub-abc", "currency": "USD", "product_price_in_cents": 29900, "current_period_ends_at": "2026-11-15T10:00:00Z", "next_assessment_at": "2026-11-15T10:00:00Z", "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 } } }""";

    private static (ISubscriptionBillingGateway Gateway, StubHandler Stub) Build(
        Func<HttpRequestMessage, HttpResponseMessage> responder, Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-key",
            ["Maxio:Subdomain"] = "test-site",
            ["Maxio:ProductFamilyHandle"] = "test-family"
        };
        foreach (var pair in overrides ?? new Dictionary<string, string?>())
        {
            settings[pair.Key] = pair.Value;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddMaxioBilling(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        var stub = new StubHandler(responder);
        services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => stub);

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<ISubscriptionBillingGateway>(), stub);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Products(int count, int offset = 0, bool archived = false) =>
        "[" + string.Join(",", Enumerable.Range(offset, count).Select(i =>
            $$"""{ "product": { "id": {{i}}, "handle": "plan-{{i}}", "name": "Plan {{i}}", "price_in_cents": {{i * 100}}, "interval": 1, "interval_unit": "month"{{(archived ? ", \"archived_at\": \"2026-01-01T00:00:00Z\"" : "")}} } }""")) + "]";

    [TestMethod]
    public async Task DerivesBaseAddressFromSubdomainAndSendsBasicAuth()
    {
        var (gateway, stub) = Build(_ => Json(HttpStatusCode.OK, Products(2)));

        var catalog = await gateway.GetPlanCatalogAsync(CancellationToken.None);

        Assert.AreEqual(2, catalog.Plans.Count);
        var request = stub.Requests.Single();
        Assert.AreEqual("test-site.chargify.com", request.RequestUri!.Host);
        StringAssert.Contains(Uri.UnescapeDataString(request.RequestUri.AbsolutePath), "/product_families/handle:test-family/products.json");
        Assert.AreEqual("Basic", request.Headers.Authorization!.Scheme);
        Assert.AreEqual("test-key:x", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
    }

    [TestMethod]
    public async Task UsesBaseUrlOverrideVerbatimWhenSet()
    {
        var (gateway, stub) = Build(_ => Json(HttpStatusCode.OK, Products(1)),
            new Dictionary<string, string?> { ["Maxio:BaseUrl"] = "https://billing-proxy.example.test" });

        await gateway.GetPlanCatalogAsync(CancellationToken.None);

        Assert.AreEqual("billing-proxy.example.test", stub.Requests.Single().RequestUri!.Host);
    }

    [TestMethod]
    public async Task PlanCatalogExcludesArchivedProducts()
    {
        var (gateway, _) = Build(_ => Json(HttpStatusCode.OK,
            Products(2).TrimEnd(']') + "," + Products(1, offset: 9, archived: true).TrimStart('[')));

        var catalog = await gateway.GetPlanCatalogAsync(CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { "plan-0", "plan-1" }, catalog.Plans.Select(p => p.Handle).ToArray());
        Assert.IsFalse(catalog.Truncated);
        Assert.AreEqual(100, catalog.Plans[1].PriceInCents);
        Assert.AreEqual("month", catalog.Plans[1].IntervalUnit);
    }

    [TestMethod]
    public async Task PlanCatalogReportsTruncationWhenPageCapIsHit()
    {
        var (gateway, stub) = Build(_ => Json(HttpStatusCode.OK, Products(MaxioSubscriptionBillingGateway.PlanPageSize)));

        var catalog = await gateway.GetPlanCatalogAsync(CancellationToken.None);

        Assert.IsTrue(catalog.Truncated);
        Assert.AreEqual(MaxioSubscriptionBillingGateway.MaxPlanPages, stub.Requests.Count);
    }

    [TestMethod]
    public async Task UnauthorizedIsReportedAsMisconfiguration()
    {
        var (gateway, _) = Build(_ => Json(HttpStatusCode.Unauthorized, "{}"));

        var ex = await Assert.ThrowsExceptionAsync<BillingProviderException>(() => gateway.GetPlanCatalogAsync(CancellationToken.None));

        Assert.AreEqual(BillingFailureKind.Misconfigured, ex.Kind);
        Assert.IsFalse(ex.Message.Contains("chargify"), "caller-facing message must not leak the provider URL");
    }

    [TestMethod]
    public async Task FindCustomerReturnsNullWhenReferenceIsUnknown()
    {
        var (gateway, stub) = Build(_ => Json(HttpStatusCode.NotFound, "{}"));

        var id = await gateway.FindCustomerIdAsync("eshop:someone@example.com", CancellationToken.None);

        Assert.IsNull(id);
        StringAssert.Contains(stub.Requests.Single().RequestUri!.AbsolutePath, "/customers/lookup.json");
    }

    [TestMethod]
    public async Task CreateSubscriptionSendsPlanHandleCustomerReferenceAndRemittance()
    {
        var (gateway, stub) = Build(_ => Json(HttpStatusCode.Created, SubscriptionJson));

        var subscription = await gateway.CreateSubscriptionAsync(42, "eshop-pro", "eshop-sub-abc", CancellationToken.None);

        Assert.AreEqual(501, subscription.Id);
        Assert.AreEqual("active", subscription.State);
        Assert.IsTrue(subscription.IsLive);
        Assert.AreEqual(29900, subscription.PriceInCents);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero), subscription.NextBillingAt);

        var request = stub.Requests.Single();
        Assert.AreEqual(HttpMethod.Post, request.Method);
        Assert.AreEqual("/subscriptions.json", request.RequestUri!.AbsolutePath);
        var body = stub.Bodies.Single()!;
        StringAssert.Contains(body, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(body, "\"customer_id\":42");
        StringAssert.Contains(body, "\"reference\":\"eshop-sub-abc\"");
        StringAssert.Contains(body, "\"payment_collection_method\":\"remittance\"");
    }

    [TestMethod]
    public async Task CreateSubscriptionConnectionFailureIsSettledByReference()
    {
        var (gateway, stub) = Build(request => request.Method == HttpMethod.Post
            ? throw new HttpRequestException("connection reset")
            : Json(HttpStatusCode.OK, SubscriptionJson));

        var subscription = await gateway.CreateSubscriptionAsync(42, "eshop-pro", "eshop-sub-abc", CancellationToken.None);

        Assert.AreEqual(501, subscription.Id);
        Assert.AreEqual(1, stub.Requests.Count(r => r.Method == HttpMethod.Post), "the write must never be resent");
        var lookup = stub.Requests.Single(r => r.Method == HttpMethod.Get);
        Assert.AreEqual("/subscriptions/lookup.json", lookup.RequestUri!.AbsolutePath);
        StringAssert.Contains(lookup.RequestUri.Query, "reference=eshop-sub-abc");
    }

    [TestMethod]
    public async Task CreateSubscriptionConnectionFailureNotFoundIsUnknownNotFailed()
    {
        var (gateway, stub) = Build(request => request.Method == HttpMethod.Post
            ? throw new HttpRequestException("connection reset")
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsExceptionAsync<BillingOutcomeUnknownException>(
            () => gateway.CreateSubscriptionAsync(42, "eshop-pro", "eshop-sub-abc", CancellationToken.None));

        Assert.AreEqual("eshop-sub-abc", ex.Reference);
        Assert.AreEqual(1, stub.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task CreateSubscriptionValidationErrorIsRejectedWithProviderMessages()
    {
        var (gateway, _) = Build(_ => Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["No payment method was on file"] }"""));

        var ex = await Assert.ThrowsExceptionAsync<BillingProviderException>(
            () => gateway.CreateSubscriptionAsync(42, "eshop-pro", "eshop-sub-abc", CancellationToken.None));

        Assert.AreEqual(BillingFailureKind.Rejected, ex.Kind);
        Assert.AreEqual(422, ex.ProviderStatusCode);
        CollectionAssert.Contains(ex.ProviderErrors.ToList(), "No payment method was on file");
    }

    [TestMethod]
    public async Task CreateCustomerConnectionFailureIsSettledByReference()
    {
        var (gateway, stub) = Build(request => request.Method == HttpMethod.Post
            ? throw new HttpRequestException("connection reset")
            : Json(HttpStatusCode.OK, """{ "customer": { "id": 77, "reference": "eshop:demouser@microsoft.com" } }"""));

        var id = await gateway.CreateCustomerAsync("eshop:demouser@microsoft.com",
            new BillingSubscriber("demouser@microsoft.com", "demouser@microsoft.com"), CancellationToken.None);

        Assert.AreEqual(77, id);
        Assert.AreEqual(1, stub.Requests.Count(r => r.Method == HttpMethod.Post));
        StringAssert.Contains(stub.Requests.Single(r => r.Method == HttpMethod.Get).RequestUri!.AbsolutePath, "/customers/lookup.json");
    }

    [TestMethod]
    public async Task CreateCustomerSendsRequiredFieldsAndReference()
    {
        var (gateway, stub) = Build(_ => Json(HttpStatusCode.Created, """{ "customer": { "id": 77 } }"""));

        await gateway.CreateCustomerAsync("eshop:demouser@microsoft.com",
            new BillingSubscriber("demouser@microsoft.com", "demouser@microsoft.com"), CancellationToken.None);

        var body = stub.Bodies.Single()!;
        StringAssert.Contains(body, "\"first_name\":\"demouser\"");
        StringAssert.Contains(body, "\"last_name\":");
        StringAssert.Contains(body, "\"email\":\"demouser@microsoft.com\"");
        StringAssert.Contains(body, "\"reference\":\"eshop:demouser@microsoft.com\"");
    }

    [TestMethod]
    [DataRow("Maxio:ApiKey", "")]
    [DataRow("Maxio:ProductFamilyHandle", " ")]
    [DataRow("Maxio:Subdomain", "")]
    [DataRow("Maxio:BaseUrl", "not-a-url")]
    public void InvalidSettingsFailValidation(string key, string value)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddMaxioBilling(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-key",
            ["Maxio:Subdomain"] = "test-site",
            ["Maxio:ProductFamilyHandle"] = "test-family",
            [key] = value
        }).Build());

        var ex = Assert.ThrowsException<OptionsValidationException>(
            () => services.BuildServiceProvider().GetRequiredService<IOptions<MaxioSettings>>().Value);
        StringAssert.Contains(ex.Message, key);
    }
}
