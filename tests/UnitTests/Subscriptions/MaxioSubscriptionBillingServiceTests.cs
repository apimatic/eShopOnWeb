using System.Collections.Concurrent;
using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Subscriptions;

public class MaxioSubscriptionBillingServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public ConcurrentQueue<string> Calls { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Enqueue(request.Method + " " + request.RequestUri!.AbsolutePath);
            await Task.Delay(50, ct); // widen the race window
            var response = Responder(request);
            response.RequestMessage = request;
            return response;
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string Products =
        "[{\"product\":{\"id\":1,\"handle\":\"plan-a\",\"name\":\"Plan A\",\"price_in_cents\":2900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false}}]";
    private const string SubscriptionJson =
        "{\"subscription\":{\"id\":77,\"state\":\"active\",\"product_price_in_cents\":2900,\"product\":{\"handle\":\"plan-a\",\"name\":\"Plan A\"}}}";

    private static (Func<MaxioSubscriptionBillingService> NewService, StubHandler Handler) Build(string db)
    {
        var handler = new StubHandler();
        handler.Responder = r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path.EndsWith("/products.json")) return Json(HttpStatusCode.OK, Products);
            if (path.EndsWith("/customers/lookup.json")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (path.EndsWith("/customers.json")) return Json(HttpStatusCode.Created, "{\"customer\":{\"id\":5}}");
            if (path.EndsWith("/subscriptions/lookup.json")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (path.EndsWith("/subscriptions.json")) return Json(HttpStatusCode.Created, SubscriptionJson);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test", Password = "x" }
        });
        var dbOptions = new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(db).Options;
        var settings = Options.Create(new MaxioSettings { ProductFamilyHandle = "fam", Subdomain = "site", ApiKey = "test" });
        return (() => new MaxioSubscriptionBillingService(client, new CatalogContext(dbOptions), settings,
            NullLogger<MaxioSubscriptionBillingService>.Instance), handler);
    }

    [Fact]
    public async Task ConcurrentSubscribe_CreatesExactlyOneCustomerAndSubscription()
    {
        var (newService, handler) = Build(nameof(ConcurrentSubscribe_CreatesExactlyOneCustomerAndSubscription));
        var user = new BillingUser("user-1", "a@b.test", "a@b.test");

        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => newService().SubscribeAsync(user, "plan-a", CancellationToken.None))));

        Assert.Equal(1, handler.Calls.Count(c => c == "POST /customers.json"));
        Assert.Equal(1, handler.Calls.Count(c => c == "POST /subscriptions.json"));
        Assert.Equal(1, results.Count(r => r.Outcome == SubscribeOutcome.Created));
        Assert.All(results.Where(r => r.Outcome != SubscribeOutcome.Created),
            r => Assert.Contains(r.Outcome, new[] { SubscribeOutcome.InProgress, SubscribeOutcome.Existing }));
    }

    [Fact]
    public async Task Subscribe_RejectsPlanThatIsNotListed()
    {
        var (newService, handler) = Build(nameof(Subscribe_RejectsPlanThatIsNotListed));

        var result = await newService().SubscribeAsync(new BillingUser("user-2", null, null), "not-listed", CancellationToken.None);

        Assert.Equal(SubscribeOutcome.PlanNotAvailable, result.Outcome);
        Assert.DoesNotContain(handler.Calls, c => c.StartsWith("POST"));
    }

    [Fact]
    public async Task Subscribe_WhenConnectionFailsAfterSend_ReportsNoDuplicateAndMarksOutcomeUnknown()
    {
        var (newService, handler) = Build(nameof(Subscribe_WhenConnectionFailsAfterSend_ReportsNoDuplicateAndMarksOutcomeUnknown));
        var original = handler.Responder;
        handler.Responder = r => r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json") && r.Method == HttpMethod.Post
            ? throw new HttpRequestException("connection reset")
            : original(r);
        var user = new BillingUser("user-3", "c@d.test", "c@d.test");

        var result = await newService().SubscribeAsync(user, "plan-a", CancellationToken.None);

        Assert.Equal(SubscribeOutcome.ProviderUnavailable, result.Outcome);
        Assert.Equal(1, handler.Calls.Count(c => c == "POST /subscriptions.json")); // never resent
        var mine = await newService().ListMySubscriptionsAsync(user, CancellationToken.None);
        Assert.Single(mine); // the claim row survives as "unconfirmed", to be settled by reference
    }

    [Fact]
    public void Settings_RefuseToStartWithoutCredentials()
    {
        Assert.Throws<InvalidOperationException>(() => new MaxioSettings { Subdomain = "s", ProductFamilyHandle = "f", ApiKey = " " }.Validate());
        new MaxioSettings { Subdomain = "s", ProductFamilyHandle = "f", ApiKey = "k" }.Validate();
    }
}
