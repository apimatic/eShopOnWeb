using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

[TestClass]
public class SubscriptionBillingTests
{
    [TestMethod]
    public async Task CustomerClaim_PrimaryKeyRejectsTheSecondInsert()
    {
        var database = Guid.NewGuid().ToString("N");
        await using var first = new CatalogContext(ContextOptions(database));
        await using var second = new CatalogContext(ContextOptions(database));
        var winner = new BillingClaimStore(first);
        var loser = new BillingClaimStore(second);

        Assert.IsTrue(await winner.TryInsertCustomerAsync("user-1", CancellationToken.None));
        Assert.IsFalse(await loser.TryInsertCustomerAsync("user-1", CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateCustomer_WhenConnectionFails_RereadsByReference()
    {
        var handler = new ScriptedHandler();
        handler.When("GET", "/products.json", _ => Json(HttpStatusCode.OK, ProductJson("eshop-pro")));
        handler.When("GET", "/customers/lookup.json", request =>
        {
            var lookups = handler.Requests.Count(item => item.RequestUri!.AbsolutePath.EndsWith("/customers/lookup.json"));
            return lookups <= 1
                ? Json(HttpStatusCode.NotFound, "")
                : Json(HttpStatusCode.OK, CustomerJson(42, ReferenceOf(request)));
        });
        handler.When("POST", "/customers.json", _ => throw new HttpRequestException("connection reset"));
        handler.When("GET", "/subscriptions/lookup.json", _ => Json(HttpStatusCode.NotFound, ""));
        handler.When("POST", "/subscriptions.json", _ => Json(HttpStatusCode.Created, SubscriptionJson(7, "user:eshop-pro", "active")));

        var service = Service(handler, "user-db");
        var result = await service.SubscribeAsync(Shopper("shopper-1"), "eshop-pro", CancellationToken.None);

        Assert.AreEqual(7, result.Subscription.Id);
        Assert.AreEqual(1, handler.Requests.Count(item => item.Method == HttpMethod.Post && item.RequestUri!.AbsolutePath.EndsWith("/customers.json")));
        Assert.IsTrue(handler.Requests.Count(item => item.Method == HttpMethod.Get && item.RequestUri!.AbsolutePath.EndsWith("/customers/lookup.json")) >= 2);
    }

    [TestMethod]
    public async Task CreateCustomer_WhenConnectionFailsAndLookupMisses_RecordsUnknown()
    {
        var handler = new ScriptedHandler();
        handler.When("GET", "/products.json", _ => Json(HttpStatusCode.OK, ProductJson("eshop-pro")));
        handler.When("GET", "/customers/lookup.json", _ => Json(HttpStatusCode.NotFound, ""));
        handler.When("POST", "/customers.json", _ => throw new HttpRequestException("connection reset"));

        var database = "unknown-customer";
        var service = Service(handler, database);
        var ex = await Assert.ThrowsExceptionAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(Shopper("shopper-1"), "eshop-pro", CancellationToken.None));

        Assert.AreEqual(503, ex.StatusCode);
        Assert.AreEqual(1, handler.Requests.Count(item => item.Method == HttpMethod.Post && item.RequestUri!.AbsolutePath.EndsWith("/customers.json")));
        await using var db = new CatalogContext(ContextOptions(database));
        var claim = await db.MaxioCustomerClaims.SingleAsync();
        Assert.AreEqual(BillingClaimStates.Unknown, claim.State);
    }

    [TestMethod]
    public async Task CreateSubscription_WhenConnectionFails_RereadsByReference()
    {
        var handler = new ScriptedHandler();
        handler.When("GET", "/products.json", _ => Json(HttpStatusCode.OK, ProductJson("eshop-pro")));
        handler.When("GET", "/customers/lookup.json", _ => Json(HttpStatusCode.OK, CustomerJson(42, "shopper-1")));
        var subscriptionLookups = 0;
        handler.When("GET", "/subscriptions/lookup.json", _ =>
        {
            subscriptionLookups++;
            return subscriptionLookups == 1
                ? Json(HttpStatusCode.NotFound, "")
                : Json(HttpStatusCode.OK, SubscriptionJson(9, "shopper-1:eshop-pro", "active"));
        });
        handler.When("POST", "/subscriptions.json", _ => throw new HttpRequestException("connection reset"));

        var service = Service(handler, "sub-recover");
        var result = await service.SubscribeAsync(Shopper("shopper-1"), "eshop-pro", CancellationToken.None);

        Assert.AreEqual(9, result.Subscription.Id);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.AreEqual(29900, result.Subscription.PriceInCents);
        Assert.IsNotNull(result.Subscription.NextBillingAt);
        Assert.AreEqual(1, handler.Requests.Count(item => item.Method == HttpMethod.Post && item.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")));
        Assert.AreEqual(0, handler.Requests.Count(item => item.Method == HttpMethod.Post && item.RequestUri!.AbsolutePath.EndsWith("/customers.json")));
    }

    [TestMethod]
    public async Task Subscribe_SecondCallDoesNotCreateAnotherSubscription()
    {
        var handler = new ScriptedHandler();
        handler.When("GET", "/products.json", _ => Json(HttpStatusCode.OK, ProductJson("eshop-pro")));
        var customerLookups = 0;
        handler.When("GET", "/customers/lookup.json", _ =>
        {
            customerLookups++;
            return customerLookups == 1
                ? Json(HttpStatusCode.NotFound, "")
                : Json(HttpStatusCode.OK, CustomerJson(42, "shopper-1"));
        });
        handler.When("POST", "/customers.json", _ => Json(HttpStatusCode.Created, CustomerJson(42, "shopper-1")));
        var subscriptionLookups = 0;
        handler.When("GET", "/subscriptions/lookup.json", _ =>
        {
            subscriptionLookups++;
            return subscriptionLookups == 1
                ? Json(HttpStatusCode.NotFound, "")
                : Json(HttpStatusCode.OK, SubscriptionJson(7, "shopper-1:eshop-pro", "active"));
        });
        handler.When("POST", "/subscriptions.json", _ => Json(HttpStatusCode.Created, SubscriptionJson(7, "shopper-1:eshop-pro", "active")));

        var service = Service(handler, "idempotent");
        var first = await service.SubscribeAsync(Shopper("shopper-1"), "eshop-pro", CancellationToken.None);
        var second = await service.SubscribeAsync(Shopper("shopper-1"), "eshop-pro", CancellationToken.None);

        Assert.IsTrue(first.Created);
        Assert.IsFalse(second.Created);
        Assert.AreEqual(first.Subscription.Id, second.Subscription.Id);
        Assert.AreEqual(1, handler.Requests.Count(item => item.Method == HttpMethod.Post && item.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")));
        Assert.AreEqual(1, handler.Requests.Count(item => item.Method == HttpMethod.Post && item.RequestUri!.AbsolutePath.EndsWith("/customers.json")));
        var enrollment = handler.Bodies.Single(body => body.Contains("\"product_handle\"", StringComparison.Ordinal));
        StringAssert.Contains(enrollment, "\"payment_collection_method\":\"remittance\"");
        Assert.IsFalse(enrollment.Contains("credit_card", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Subscribe_WhenPlanRequiresACard_DoesNotCreate()
    {
        var handler = new ScriptedHandler();
        handler.When("GET", "/products.json", _ => Json(HttpStatusCode.OK, ProductJson("eshop-pro", requireCreditCard: true)));
        var service = Service(handler, "card-required");

        var ex = await Assert.ThrowsExceptionAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(Shopper("shopper-1"), "eshop-pro", CancellationToken.None));

        Assert.AreEqual(400, ex.StatusCode);
        Assert.AreEqual(0, handler.Requests.Count(item => item.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task Subscribe_RejectsHandleThatIsNotInTheListedPlans()
    {
        var handler = new ScriptedHandler();
        handler.When("GET", "/products.json", _ => Json(HttpStatusCode.OK, ProductJson("basic-plan")));
        var service = Service(handler, "catalog-check");

        var ex = await Assert.ThrowsExceptionAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(Shopper("shopper-1"), "eshop-pro", CancellationToken.None));

        Assert.AreEqual(400, ex.StatusCode);
        Assert.AreEqual(0, handler.Requests.Count(item => item.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task ListPlans_SetsTruncatedWhenThePageCapIsHit()
    {
        var handler = new ScriptedHandler();
        handler.When("GET", "/products.json", _ => Json(HttpStatusCode.OK, ProductJson("eshop-pro")));
        var service = Service(handler, "truncated", new BillingRuntimeLimits { MaxPlanPages = 1, PlanPageSize = 1 });

        var page = await service.ListPlansAsync(CancellationToken.None);

        Assert.IsTrue(page.Truncated);
        Assert.AreEqual(1, page.Plans.Count);
        Assert.AreEqual("month", page.Plans[0].IntervalUnit);
        Assert.AreEqual(29900L, page.Plans[0].PriceInCents);
    }

    private static MaxioSubscriptionBillingService Service(ScriptedHandler handler, string database, BillingRuntimeLimits? limits = null)
    {
        var context = new CatalogContext(ContextOptions(database));
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        return new MaxioSubscriptionBillingService(
            client,
            new BillingClaimStore(context),
            Options.Create(new MaxioOptions { ApiKey = "test", Subdomain = "test-site", ProductFamilyHandle = "eshop-subscribe" }),
            limits ?? new BillingRuntimeLimits(),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static DbContextOptions<CatalogContext> ContextOptions(string database) =>
        new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(database).Options;

    private static ShopperAccount Shopper(string userId) =>
        new() { UserId = userId, Email = userId + "@shop.example" };

    private static string ReferenceOf(HttpRequestMessage request) =>
        Uri.UnescapeDataString(request.RequestUri!.Query.TrimStart('?').Split('&').First(part => part.StartsWith("reference="))["reference=".Length..]);

    private static string ProductJson(string handle, bool? requireCreditCard = null)
    {
        var card = requireCreditCard is null ? "" : ", \"require_credit_card\": " + (requireCreditCard.Value ? "true" : "false");
        return "[{ \"product\": { \"id\": 1, \"name\": \"Pro\", \"handle\": \"" + handle + "\", \"description\": \"Plan\", \"price_in_cents\": 29900, \"interval\": 1, \"interval_unit\": \"month\"" + card + " } }]";
    }

    private static string CustomerJson(int id, string reference) =>
        "{ \"customer\": { \"id\": " + id + ", \"reference\": \"" + reference + "\", \"email\": \"a@b.c\", \"first_name\": \"A\", \"last_name\": \"B\" } }";

    private static string SubscriptionJson(int id, string reference, string state) =>
        "{ \"subscription\": { \"id\": " + id + ", \"state\": \"" + state + "\", \"product_price_in_cents\": 29900, \"current_period_ends_at\": \"2026-11-16T00:00:00+00:00\", \"currency\": \"USD\", \"reference\": \"" + reference + "\", \"product\": { \"id\": 1, \"name\": \"Pro\", \"handle\": \"eshop-pro\", \"price_in_cents\": 29900 } } }";

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public System.Collections.Generic.List<HttpRequestMessage> Requests { get; } = new();
        public System.Collections.Generic.List<string> Bodies { get; } = new();
        private readonly System.Collections.Generic.List<(string Method, string Path, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = new();

        public void When(string method, string path, Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            _routes.Add((method, path, respond));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            var path = request.RequestUri?.AbsolutePath ?? "";
            foreach (var route in _routes)
            {
                if (request.Method.Method == route.Method && path.EndsWith(route.Path, StringComparison.Ordinal))
                {
                    var response = route.Respond(request);
                    response.RequestMessage = request;
                    return response;
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
        }
    }
}
