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
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class MaxioSubscriptionServiceTests
{
    private const string PlansJson = """[{"product":{"id":1,"handle":"eshop-pro","name":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""";
    private const string CustomerJson = """{"customer":{"id":9,"reference":"u@x.com","email":"u@x.com","first_name":"u","last_name":"Customer"}}""";
    private const string SubscriptionJson = """{"subscription":{"id":5,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-12-01T00:00:00Z","product":{"id":1,"handle":"eshop-pro","name":"Pro"},"customer":{"id":9}}}""";

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
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (MaxioSubscriptionService Service, CatalogContext Db, StubHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
        });
        var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var settings = new MaxioSettings { ApiKey = "test-key", Subdomain = "test", ProductFamilyHandle = "family" };
        return (new MaxioSubscriptionService(client, settings, db, NullLogger<MaxioSubscriptionService>.Instance), db, handler);
    }

    [Fact]
    public async Task UnknownPlan_IsRejectedBeforeAnyClaimOrWrite()
    {
        var (service, db, handler) = Create(r => r.RequestUri!.AbsolutePath.Contains("products")
            ? Json(HttpStatusCode.OK, PlansJson) : Json(HttpStatusCode.InternalServerError, "{}"));

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync("u@x.com", "u@x.com", "not-a-plan", default));

        Assert.Empty(db.MaxioSubscriptionLinks);
        Assert.DoesNotContain(handler.Calls, c => c.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ConnectionFailureOnCreate_IsSettledByReRead_WhenMaxioHoldsTheSubscription()
    {
        var (service, db, handler) = Create(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path.Contains("products")) return Json(HttpStatusCode.OK, PlansJson);
            if (path.Contains("customers/lookup")) return Json(HttpStatusCode.OK, CustomerJson);
            if (path.Contains("subscriptions/lookup")) return Json(HttpStatusCode.OK, SubscriptionJson);
            if (r.Method == HttpMethod.Post) throw new HttpRequestException("connection reset");
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var info = await service.SubscribeAsync("u@x.com", "u@x.com", "eshop-pro", default);

        Assert.Equal(5, info.SubscriptionId);
        Assert.Equal(1, handler.Calls.Count(c => c.Method == HttpMethod.Post)); // POST is never resent
        var link = Assert.Single(db.MaxioSubscriptionLinks);
        Assert.Equal(SubscriptionLinkState.Completed, link.State);
    }

    [Fact]
    public async Task ConnectionFailureOnCreate_ReleasesClaim_WhenMaxioHasNothing()
    {
        var (service, db, _) = Create(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path.Contains("products")) return Json(HttpStatusCode.OK, PlansJson);
            if (path.Contains("customers/lookup")) return Json(HttpStatusCode.OK, CustomerJson);
            if (path.Contains("subscriptions/lookup")) return Json(HttpStatusCode.NotFound, "{}");
            if (path.Contains("customers/9/subscriptions")) return Json(HttpStatusCode.OK, "[]");
            if (r.Method == HttpMethod.Post) throw new HttpRequestException("connection reset");
            return Json(HttpStatusCode.NotFound, "{}");
        });

        await Assert.ThrowsAsync<BillingUnavailableException>(
            () => service.SubscribeAsync("u@x.com", "u@x.com", "eshop-pro", default));

        Assert.Empty(db.MaxioSubscriptionLinks);
    }

    [Fact]
    public async Task SecondClaimForSameUserAndPlan_IsRefusedWithConflict()
    {
        var (service, db, handler) = Create(r => r.RequestUri!.AbsolutePath.Contains("products")
            ? Json(HttpStatusCode.OK, PlansJson) : Json(HttpStatusCode.InternalServerError, "{}"));
        db.MaxioSubscriptionLinks.Add(new MaxioSubscriptionLink("u@x.com", "eshop-pro", "eshop:u@x.com:eshop-pro"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear(); // the service runs in its own request scope

        await Assert.ThrowsAsync<DuplicateException>(
            () => service.SubscribeAsync("u@x.com", "u@x.com", "eshop-pro", default));

        Assert.DoesNotContain(handler.Calls, c => c.Method == HttpMethod.Post);
    }
}
