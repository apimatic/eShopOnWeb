using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class MaxioSubscriptionServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<string> Calls { get; } = new();
        public bool CustomerExists { get; set; }
        public volatile bool SubscriptionExists;
        private const string SubJson = """{"subscription":{"id":99,"state":"active","next_assessment_at":"2030-01-01T00:00:00Z","product":{"id":1,"handle":"pro","name":"Pro","price_in_cents":29900}}}""";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Calls) Calls.Add($"{request.Method} {path}");
            HttpResponseMessage Json(HttpStatusCode code, string body) => new(code)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };

            if (path.Contains("/product_families/") && path.EndsWith("/products.json"))
                return Task.FromResult(Json(HttpStatusCode.OK,
                    """[{"product":{"id":1,"handle":"pro","name":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]"""));
            if (path.EndsWith("/customers/lookup.json"))
                return Task.FromResult(CustomerExists
                    ? Json(HttpStatusCode.OK, """{"customer":{"id":7,"reference":"r"}}""")
                    : Json(HttpStatusCode.NotFound, "{}"));
            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
            {
                CustomerExists = true;
                return Task.FromResult(Json(HttpStatusCode.Created, """{"customer":{"id":7,"reference":"r"}}"""));
            }
            if (path.EndsWith("/subscriptions/lookup.json"))
                return Task.FromResult(SubscriptionExists ? Json(HttpStatusCode.OK, SubJson) : Json(HttpStatusCode.NotFound, "{}"));
            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                Thread.Sleep(150); // widen the race window
                SubscriptionExists = true;
                return Task.FromResult(Json(HttpStatusCode.Created,
                    """{"subscription":{"id":99,"state":"active","next_assessment_at":"2030-01-01T00:00:00Z","product":{"id":1,"handle":"pro","name":"Pro","price_in_cents":29900}}}"""));
            }
            return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
        }
    }

    private static (Func<MaxioSubscriptionService> Create, StubHandler Handler) Build()
    {
        var handler = new StubHandler();
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "k", Password = "x" },
        });
        var dbName = Guid.NewGuid().ToString();
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "fam",
        });
        MaxioSubscriptionService Create()
        {
            var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(dbName).Options);
            return new MaxioSubscriptionService(client, db, settings, NullLogger<MaxioSubscriptionService>.Instance);
        }
        return (Create, handler);
    }

    [TestMethod]
    public async Task ConcurrentSubscribe_SendsExactlyOneCreateSubscription()
    {
        var (create, handler) = Build();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { return (await create().SubscribeAsync("u@x.com", "u@x.com", "pro", default)).Created ? "created" : "existing"; }
            catch (SubscriptionException ex) { return ex.StatusCode.ToString(); }
        }));

        Assert.AreEqual(1, handler.Calls.Count(c => c == "POST /subscriptions.json"));
        Assert.AreEqual(1, handler.Calls.Count(c => c == "POST /customers.json"));
        Assert.AreEqual(1, outcomes.Count(o => o == "created"));
    }

    [TestMethod]
    public async Task RepeatSubscribe_ReturnsExistingWithoutNewWrite()
    {
        var (create, handler) = Build();
        await create().SubscribeAsync("u@x.com", "u@x.com", "pro", default);

        var again = await create().SubscribeAsync("u@x.com", "u@x.com", "pro", default);

        Assert.IsFalse(again.Created);
        Assert.AreEqual(1, handler.Calls.Count(c => c == "POST /subscriptions.json"));
    }

    [TestMethod]
    public async Task UnknownPlan_IsRejectedBeforeAnyWrite()
    {
        var (create, handler) = Build();

        var ex = await Assert.ThrowsExceptionAsync<SubscriptionException>(
            () => create().SubscribeAsync("u@x.com", "u@x.com", "not-offered", default));

        Assert.AreEqual(400, ex.StatusCode);
        Assert.IsFalse(handler.Calls.Any(c => c.StartsWith("POST")));
    }
}
