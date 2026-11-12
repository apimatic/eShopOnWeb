using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace PublicApiIntegrationTests.SubscriptionTests;

[TestClass]
public class SubscriptionServiceTests
{
    private const string PlansJson = """[{"product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""";
    private const string CustomerJson = """{"customer":{"id":7,"reference":"user-1"}}""";
    private const string SubscriptionJson = """{"subscription":{"id":9,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2030-01-01T00:00:00Z","product":{"handle":"eshop-pro","name":"Pro Plan"},"customer":{"id":7}}}""";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<(HttpMethod Method, string Path)> Requests { get; } = new();
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        public int Posts(string path) => Requests.Count(r => r.Method == HttpMethod.Post && r.Path.EndsWith(path));
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Happy(HttpRequestMessage r)
    {
        var p = r.RequestUri!.AbsolutePath;
        if (r.Method == HttpMethod.Get && p.EndsWith("/products.json")) return Json(HttpStatusCode.OK, PlansJson);
        if (r.Method == HttpMethod.Get && p.EndsWith("/lookup.json")) return Json(HttpStatusCode.NotFound, "{}");
        if (r.Method == HttpMethod.Get && p.EndsWith("/subscriptions/9.json")) return Json(HttpStatusCode.OK, SubscriptionJson);
        if (r.Method == HttpMethod.Post && p.EndsWith("/customers.json")) return Json(HttpStatusCode.Created, CustomerJson);
        if (r.Method == HttpMethod.Post && p.EndsWith("/subscriptions.json")) return Json(HttpStatusCode.Created, SubscriptionJson);
        return Json(HttpStatusCode.InternalServerError, "{}");
    }

    private static SubscriptionService Build(StubHandler handler, string dbName, MaxioSettings? settings = null)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test", Password = "x" }
        });
        var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(dbName).Options);
        var users = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        users.FindByNameAsync("shopper@example.com").Returns(new ApplicationUser { Id = "user-1", UserName = "shopper@example.com", Email = "shopper@example.com" });
        settings ??= new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "fam" };
        return new SubscriptionService(client, settings, db, users, NullLogger<SubscriptionService>.Instance);
    }

    private static ClaimsPrincipal Caller() =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "shopper@example.com") }, "test"));

    [TestMethod]
    public async Task Subscribe_Twice_CreatesOneSubscription()
    {
        var handler = new StubHandler(Happy);
        var db = Guid.NewGuid().ToString();

        var first = await Build(handler, db).SubscribeAsync(Caller(), "eshop-pro", default);
        var second = await Build(handler, db).SubscribeAsync(Caller(), "eshop-pro", default);

        Assert.IsTrue(first.Created);
        Assert.IsFalse(second.Created);
        Assert.AreEqual(9, second.Subscription.Id);
        Assert.AreEqual(1, handler.Posts("/subscriptions.json"));
        Assert.AreEqual(1, handler.Posts("/customers.json"));
        Assert.AreEqual("active", first.Subscription.State);
        Assert.AreEqual(299m, first.Subscription.Price);
    }

    [TestMethod]
    public async Task Subscribe_UnknownPlan_IsRejectedWithoutWrites()
    {
        var handler = new StubHandler(Happy);
        var ex = await Assert.ThrowsExceptionAsync<SubscriptionException>(
            () => Build(handler, Guid.NewGuid().ToString()).SubscribeAsync(Caller(), "not-a-plan", default));

        Assert.AreEqual(400, ex.StatusCode);
        Assert.AreEqual(0, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task Subscribe_UsesConfiguredDefaultPlan_WhenNoneGiven()
    {
        var handler = new StubHandler(Happy);
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "fam", DefaultPlanHandle = "eshop-pro" };

        var result = await Build(handler, Guid.NewGuid().ToString(), settings).SubscribeAsync(Caller(), null, default);

        Assert.AreEqual("eshop-pro", result.Subscription.PlanHandle);
    }

    [TestMethod]
    public async Task Subscribe_ConnectionDropsAfterWrite_SettlesByReferenceWithoutResending()
    {
        var posted = false;
        var handler = new StubHandler(r =>
        {
            var p = r.RequestUri!.AbsolutePath;
            if (r.Method == HttpMethod.Get && p.EndsWith("/products.json")) return Json(HttpStatusCode.OK, PlansJson);
            if (r.Method == HttpMethod.Get && p.EndsWith("/customers/lookup.json")) return Json(HttpStatusCode.OK, CustomerJson);
            if (r.Method == HttpMethod.Get && p.EndsWith("/subscriptions/lookup.json"))
                return posted ? Json(HttpStatusCode.OK, SubscriptionJson) : Json(HttpStatusCode.NotFound, "{}");
            if (r.Method == HttpMethod.Post && p.EndsWith("/subscriptions.json"))
            {
                posted = true; // the provider acted, then the connection died
                throw new HttpRequestException("connection reset");
            }
            return Json(HttpStatusCode.InternalServerError, "{}");
        });

        var result = await Build(handler, Guid.NewGuid().ToString()).SubscribeAsync(Caller(), "eshop-pro", default);

        Assert.AreEqual(9, result.Subscription.Id);
        Assert.AreEqual(1, handler.Posts("/subscriptions.json"));
    }

    [TestMethod]
    public async Task Subscribe_ProviderRejects_ReleasesClaimAndSurfacesStatus()
    {
        var reject = true;
        var handler = new StubHandler(r =>
        {
            var p = r.RequestUri!.AbsolutePath;
            if (reject && r.Method == HttpMethod.Post && p.EndsWith("/subscriptions.json"))
                return Json(HttpStatusCode.UnprocessableEntity, """{"errors":["bad plan"]}""");
            return Happy(r);
        });
        var db = Guid.NewGuid().ToString();

        var ex = await Assert.ThrowsExceptionAsync<SubscriptionException>(
            () => Build(handler, db).SubscribeAsync(Caller(), "eshop-pro", default));
        Assert.AreEqual(422, ex.StatusCode);
        StringAssert.Contains(ex.Message, "bad plan");

        reject = false; // the claim was released, so a retry is allowed
        var result = await Build(handler, db).SubscribeAsync(Caller(), "eshop-pro", default);
        Assert.IsTrue(result.Created);
    }

    [TestMethod]
    public void Settings_RefuseBlankCredentials()
    {
        Assert.IsNotNull(new MaxioSettings { Subdomain = "s", ProductFamilyHandle = "f" }.Validate());
        Assert.IsNotNull(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f" }.Validate());
        Assert.IsNull(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "https://x.example.com" }.Validate());
    }
}
