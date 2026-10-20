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
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class SubscriptionServiceTests
{
    private const string PRODUCTS = """[{"product":{"handle":"plan-a","name":"Plan A","price_in_cents":900,"interval":1,"interval_unit":"month"}}]""";
    private const string CUSTOMER = """{"customer":{"id":11,"reference":"u1"}}""";
    private const string SUBSCRIPTION = """{"subscription":{"id":5,"state":"active","product_price_in_cents":900,"next_assessment_at":"2026-11-20T00:00:00Z","reference":"x","product":{"handle":"plan-a","name":"Plan A"}}}""";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<(HttpMethod Method, Uri Uri)> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests) Requests.Add((request.Method, request.RequestUri!));
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        public int Count(HttpMethod m, string pathPart) =>
            Requests.Count(r => r.Method == m && r.Uri.AbsolutePath.Contains(pathPart));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (SubscriptionService Service, CatalogContext Db, StubHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> responder, string? baseUrl = null, string? dbName = null)
    {
        var handler = new StubHandler(responder);
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "k", Password = "x" }
        };
        if (baseUrl != null) options.Server.Production.Us.BaseUrl = baseUrl;
        else options.Server.Production.Us.Site = "test-site";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString()).Options);
        var settings = Options.Create(new MaxioSettings { ApiKey = "k", Subdomain = "test-site", ProductFamilyHandle = "fam" });
        return (new SubscriptionService(client, db, settings, NullLogger<SubscriptionService>.Instance), db, handler);
    }

    private static HttpResponseMessage Happy(HttpRequestMessage r)
    {
        var path = r.RequestUri!.AbsolutePath;
        if (path.Contains("product_families")) return Json(HttpStatusCode.OK, PRODUCTS);
        if (path.Contains("customers/lookup")) return Json(HttpStatusCode.NotFound, "{}");
        if (path.Contains("subscriptions/lookup")) return Json(HttpStatusCode.NotFound, "{}");
        if (path.EndsWith("customers.json")) return Json(HttpStatusCode.Created, CUSTOMER);
        if (path.EndsWith("subscriptions.json")) return Json(HttpStatusCode.Created, SUBSCRIPTION);
        return Json(HttpStatusCode.InternalServerError, "{}");
    }

    [TestMethod]
    public async Task ListPlans_UsesConfiguredFamilyHandle_AndBaseUrlVerbatim()
    {
        var (service, _, handler) = Build(Happy, baseUrl: "http://maxio.test:1234");

        var result = await service.ListPlansAsync(default);

        Assert.AreEqual("plan-a", result.Plans.Single().Handle);
        Assert.IsFalse(result.IsTruncated);
        var uri = handler.Requests.Single().Uri;
        Assert.AreEqual("maxio.test", uri.Host);
        Assert.AreEqual(1234, uri.Port);
        StringAssert.Contains(Uri.UnescapeDataString(uri.AbsolutePath), "handle:fam");
    }

    [TestMethod]
    public async Task Subscribe_CreatesCustomerAndSubscriptionOnce_ThenReturnsExisting()
    {
        var existingSubscription = false;
        var (service, db, handler) = Build(r =>
        {
            if (existingSubscription && r.RequestUri!.AbsolutePath.Contains("subscriptions/lookup"))
                return Json(HttpStatusCode.OK, SUBSCRIPTION);
            return Happy(r);
        });

        var first = await service.SubscribeAsync("u1", "a@b.c", "plan-a", default);
        existingSubscription = true;
        var second = await service.SubscribeAsync("u1", "a@b.c", "plan-a", default);

        Assert.IsFalse(first.AlreadySubscribed);
        Assert.AreEqual("active", first.Subscription.State);
        Assert.AreEqual(900, first.Subscription.PriceInCents);
        Assert.IsNotNull(first.Subscription.NextBillingDate);
        Assert.IsTrue(second.AlreadySubscribed);
        Assert.AreEqual(1, handler.Count(HttpMethod.Post, "customers.json"));
        Assert.AreEqual(1, handler.Count(HttpMethod.Post, "subscriptions.json"));
        Assert.AreEqual(MaxioSubscriptionLinkStatus.Created, db.MaxioSubscriptionLinks.Single().Status);
    }

    [TestMethod]
    public async Task Subscribe_PlanNotOffered_NeverCallsWriteOperations()
    {
        var (service, db, handler) = Build(Happy);

        await Assert.ThrowsExceptionAsync<PlanNotOfferedException>(
            () => service.SubscribeAsync("u1", "a@b.c", "not-a-plan", default));

        Assert.AreEqual(0, handler.Requests.Count(r => r.Method == HttpMethod.Post));
        Assert.AreEqual(0, await db.MaxioCustomerLinks.CountAsync());
    }

    [TestMethod]
    public async Task Subscribe_SecondClaimIsRefusedBeforeReachingMaxio()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, _, handler) = Build(Happy, dbName: dbName);
        var (_, other, _) = Build(Happy, dbName: dbName); // a second request's own context, same store
        other.MaxioCustomerLinks.Add(new MaxioCustomerLink { UserId = "u1" }); // first caller in flight
        await other.SaveChangesAsync();

        await Assert.ThrowsExceptionAsync<SubscriptionInProgressException>(
            () => service.SubscribeAsync("u1", "a@b.c", "plan-a", default));

        Assert.AreEqual(0, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task Subscribe_ConnectionFailureOnCreate_IsNotResent_AndOutcomeIsReconciled()
    {
        var (service, db, handler) = Build(r =>
        {
            if (r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("subscriptions.json"))
                throw new HttpRequestException("connection reset");
            return Happy(r);
        });

        await Assert.ThrowsExceptionAsync<BillingUnavailableException>(
            () => service.SubscribeAsync("u1", "a@b.c", "plan-a", default));

        Assert.AreEqual(1, handler.Count(HttpMethod.Post, "subscriptions.json")); // SDK never resends a POST
        Assert.IsTrue(handler.Count(HttpMethod.Get, "subscriptions/lookup") >= 2);   // re-read after the failure
        Assert.AreEqual(0, await db.MaxioSubscriptionLinks.CountAsync());           // not found -> claim released
    }

    [TestMethod]
    public async Task Subscribe_ConnectionFailureThenFound_RecordsTheSubscription()
    {
        var created = false;
        var (service, db, _) = Build(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (r.Method == HttpMethod.Post && path.EndsWith("subscriptions.json"))
            {
                created = true;
                throw new HttpRequestException("connection reset");
            }
            if (created && path.Contains("subscriptions/lookup")) return Json(HttpStatusCode.OK, SUBSCRIPTION);
            return Happy(r);
        });

        var result = await service.SubscribeAsync("u1", "a@b.c", "plan-a", default);

        Assert.IsTrue(result.AlreadySubscribed);
        Assert.AreEqual(MaxioSubscriptionLinkStatus.Created, db.MaxioSubscriptionLinks.Single().Status);
    }

    [TestMethod]
    public async Task Subscribe_ProviderValidationRejection_ReleasesClaim_AndSurfacesAs4xx()
    {
        var (service, db, _) = Build(r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("subscriptions.json")
                ? Json(HttpStatusCode.UnprocessableEntity, """{"errors":["No payment method was on file"]}""")
                : Happy(r));

        var ex = await Assert.ThrowsExceptionAsync<BillingRejectedException>(
            () => service.SubscribeAsync("u1", "a@b.c", "plan-a", default));

        StringAssert.Contains(ex.Message, "No payment method");
        Assert.AreEqual(0, await db.MaxioSubscriptionLinks.CountAsync());
    }

    [TestMethod]
    public async Task ListPlans_Provider401_IsNotTheCallersFault()
    {
        var (service, _, _) = Build(_ => Json(HttpStatusCode.Unauthorized, "{}"));

        await Assert.ThrowsExceptionAsync<BillingUnavailableException>(() => service.ListPlansAsync(default));
    }
}
