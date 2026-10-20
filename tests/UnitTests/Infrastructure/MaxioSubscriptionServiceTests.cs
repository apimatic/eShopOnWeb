using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class MaxioSubscriptionServiceTests
{
    private const string ProductsJson =
        """[{"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""";
    private const string SubscriptionJson =
        """{"subscription":{"id":7,"state":"active","product_price_in_cents":29900,"product":{"handle":"eshop-pro","name":"Pro Plan"}}}""";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<(HttpMethod Method, string Path)> Calls { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath));
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        public int Count(HttpMethod method, string pathPart) =>
            Calls.Count(c => c.Method == method && c.Path.Contains(pathPart));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (MaxioSubscriptionService Service, CatalogContext Db, StubHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions
        {
            Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Disabled()
        });
        var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "fam", DefaultPlanHandle = "eshop-pro"
        });
        return (new MaxioSubscriptionService(client, db, settings, NullLogger<MaxioSubscriptionService>.Instance), db, handler);
    }

    private static HttpResponseMessage Happy(HttpRequestMessage r)
    {
        var path = r.RequestUri!.AbsolutePath;
        if (path.Contains("/products")) return Json(HttpStatusCode.OK, ProductsJson);
        if (path.Contains("customers/lookup")) return Json(HttpStatusCode.NotFound, "{}");
        if (path.EndsWith("/customers.json")) return Json(HttpStatusCode.Created, """{"customer":{"id":42}}""");
        if (path.EndsWith("/subscriptions.json")) return Json(HttpStatusCode.Created, SubscriptionJson);
        return Json(HttpStatusCode.NotFound, "{}");
    }

    private static SubscribeRequest Request(string? plan = null) =>
        new("user@example.com", "user@example.com", "A", "B", plan);

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_AndRecordsClaims()
    {
        var (service, db, handler) = Create(Happy);

        var result = await service.SubscribeAsync(Request(), default);

        Assert.Equal(7, result.SubscriptionId);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal("active", result.State);
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(42, (await db.MaxioCustomerLinks.SingleAsync()).MaxioCustomerId);
        Assert.Equal(7, (await db.MaxioSubscriptionLinks.SingleAsync()).MaxioSubscriptionId);
    }

    [Fact]
    public async Task Subscribe_UnofferedPlan_IsRefusedBeforeAnyWrite()
    {
        var (service, db, handler) = Create(Happy);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(Request("not-in-family"), default));

        Assert.Equal(SubscriptionFailure.Invalid, ex.Failure);
        Assert.Equal(0, handler.Calls.Count(c => c.Method == HttpMethod.Post));
        Assert.Empty(db.MaxioCustomerLinks);
    }

    [Fact]
    public async Task Subscribe_Twice_DoesNotCreateASecondSubscription()
    {
        var (service, _, handler) = Create(r =>
            r.RequestUri!.AbsolutePath.Contains("subscriptions/lookup") ? Json(HttpStatusCode.OK, SubscriptionJson) : Happy(r));

        await service.SubscribeAsync(Request(), default);
        var again = await service.SubscribeAsync(Request(), default);

        Assert.Equal(7, again.SubscriptionId);
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task Subscribe_ProviderRefusal_ReleasesTheSubscriptionClaim()
    {
        var (service, db, _) = Create(r =>
            r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")
                ? Json((HttpStatusCode)422, """{"errors":["No payment method"]}""")
                : Happy(r));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.SubscribeAsync(Request(), default));

        Assert.Equal(SubscriptionFailure.Rejected, ex.Failure);
        Assert.Empty(db.MaxioSubscriptionLinks);
        Assert.Single(db.MaxioCustomerLinks);
    }

    [Fact]
    public async Task Subscribe_ConnectionFailureOnCreate_IsSettledByReReadingTheReference()
    {
        var (service, db, handler) = Create(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (r.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                throw new HttpRequestException("connection reset");
            if (path.Contains("subscriptions/lookup")) return Json(HttpStatusCode.OK, SubscriptionJson);
            return Happy(r);
        });

        var result = await service.SubscribeAsync(Request(), default);

        Assert.Equal(7, result.SubscriptionId);
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/subscriptions.json"));   // never resent
        Assert.Equal(7, (await db.MaxioSubscriptionLinks.SingleAsync()).MaxioSubscriptionId);
    }
}
