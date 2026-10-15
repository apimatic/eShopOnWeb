using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class MaxioSubscriptionServiceTests
{
    private const string User = "shopper@example.com";

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
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static (MaxioSubscriptionService Service, CatalogContext Db, StubHandler Handler) Build(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        // The customer link is already settled, so the identity lookup is never reached.
        db.MaxioCustomerLinks.Add(new MaxioCustomerLink { BuyerId = User, Reference = "eshop-user:" + User, MaxioCustomerId = 7, CreatedAt = DateTimeOffset.UtcNow });
        db.SaveChanges();

        var handler = new StubHandler(responder);
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test", Password = "x" },
            Retry = RetryOptions.Disabled(),
        });
        var service = new MaxioSubscriptionService(client, db, null!,
            Options.Create(new MaxioOptions { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "fam" }),
            NullLogger<MaxioSubscriptionService>.Instance);
        return (service, db, handler);
    }

    private static HttpResponseMessage Catalog(HttpRequestMessage r)
    {
        var path = r.RequestUri!.AbsolutePath;
        if (path.EndsWith("/product_families.json"))
            return Json(HttpStatusCode.OK, """[{"product_family":{"id":1,"handle":"fam"}}]""");
        if (path.Contains("/product_families/1/products"))
            return Json(HttpStatusCode.OK, """[{"product":{"id":10,"handle":"pro","name":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Subscribe_WhenConnectionFailsAfterSend_ReconcilesByReferenceAndKeepsClaimPendingWhenNotFound()
    {
        var (service, db, handler) = Build(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (r.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json")) throw new HttpRequestException("connection reset");
            if (path.EndsWith("/subscriptions/lookup.json")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (path.Contains("/customers/7/subscriptions")) return Json(HttpStatusCode.OK, "[]");
            return Catalog(r);
        });

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => service.SubscribeAsync(User, "pro", null, null, default));

        Assert.Equal(202, ex.StatusCode);
        Assert.Equal(1, handler.Requests.Count(x => x.Method == HttpMethod.Post)); // never resent
        Assert.Contains(handler.Requests, x => x.Path.EndsWith("/subscriptions/lookup.json")); // outcome re-read by reference
        var claim = await db.UserSubscriptions.SingleAsync();
        Assert.Equal(UserSubscriptionStatus.Pending, claim.Status);
    }

    [Fact]
    public async Task Subscribe_WhenConnectionFailsButSubscriptionLanded_SettlesFromMaxio()
    {
        var (service, db, _) = Build(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (r.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json")) throw new HttpRequestException("connection reset");
            if (path.EndsWith("/subscriptions/lookup.json"))
                return Json(HttpStatusCode.OK, """{"subscription":{"id":99,"state":"active","product":{"handle":"pro","name":"Pro"}}}""");
            if (path.Contains("/customers/7/subscriptions")) return Json(HttpStatusCode.OK, "[]");
            return Catalog(r);
        });

        var result = await service.SubscribeAsync(User, "pro", null, null, default);

        Assert.Equal(99, result.Subscription.SubscriptionId);
        Assert.Equal(UserSubscriptionStatus.Active, (await db.UserSubscriptions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Subscribe_WhenProviderRefuses_ReleasesClaimAndRelaysCallerError()
    {
        var (service, db, _) = Build(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (r.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json")) return Json(HttpStatusCode.UnprocessableEntity, """{"errors":["bad"]}""");
            if (path.Contains("/customers/7/subscriptions")) return Json(HttpStatusCode.OK, "[]");
            return Catalog(r);
        });

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => service.SubscribeAsync(User, "pro", null, null, default));

        Assert.Equal(422, ex.StatusCode);
        Assert.Empty(await db.UserSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Subscribe_WithPlanNotOffered_FailsBeforeAnyWrite()
    {
        var (service, _, handler) = Build(Catalog);

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => service.SubscribeAsync(User, "unknown", null, null, default));

        Assert.Equal(404, ex.StatusCode);
        Assert.DoesNotContain(handler.Requests, x => x.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Subscribe_WhenClaimAlreadyPending_ReturnsConflictWithoutSecondWrite()
    {
        var (service, db, handler) = Build(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path.EndsWith("/subscriptions/lookup.json")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            return Catalog(r);
        });
        db.UserSubscriptions.Add(new UserSubscription { BuyerId = User, PlanHandle = "pro", Reference = "abc", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => service.SubscribeAsync(User, "pro", null, null, default));

        Assert.Equal(409, ex.StatusCode);
        Assert.DoesNotContain(handler.Requests, x => x.Method == HttpMethod.Post);
    }
}
