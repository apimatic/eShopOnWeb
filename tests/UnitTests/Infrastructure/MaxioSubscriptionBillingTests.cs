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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class MaxioSubscriptionBillingTests
{
    private const string Products = """[{"product":{"id":1,"handle":"eshop-pro","name":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""";
    private const string CustomerJson = """{"customer":{"id":11,"reference":"u1","email":"a@b.c"}}""";
    private const string SubscriptionJson = """{"subscription":{"id":22,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-11-20T00:00:00Z","reference":"eshop-sub-u1","product":{"handle":"eshop-pro","name":"Pro"},"customer":{"id":11}}}""";

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

        public int Count(HttpMethod method, string pathPart) =>
            Requests.Count(r => r.Method == method && r.Path.Contains(pathPart));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (MaxioSubscriptionBilling Service, CatalogContext Db, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "k", Password = "x" }
        });
        var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var settings = Options.Create(new MaxioOptions { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "fam" });
        return (new MaxioSubscriptionBilling(client, settings, db, NullLogger<MaxioSubscriptionBilling>.Instance), db, handler);
    }

    /// <summary>Happy-path router; <paramref name="onSubscriptionPost"/> and <paramref name="onFind"/> override the interesting calls.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> Router(
        Func<HttpRequestMessage, HttpResponseMessage>? onSubscriptionPost = null,
        Func<HttpRequestMessage, HttpResponseMessage>? onFind = null)
    {
        return req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("products")) return Json(HttpStatusCode.OK, Products);
            if (path.Contains("customers/lookup")) return Json(HttpStatusCode.NotFound, "{}");
            if (path.Contains("customers") && req.Method == HttpMethod.Post) return Json(HttpStatusCode.Created, CustomerJson);
            if (path.Contains("subscriptions/lookup")) return (onFind ?? (_ => Json(HttpStatusCode.NotFound, "{}")))(req);
            if (path.Contains("subscriptions") && req.Method == HttpMethod.Post)
                return (onSubscriptionPost ?? (_ => Json(HttpStatusCode.Created, SubscriptionJson)))(req);
            return Json(HttpStatusCode.NotFound, "{}");
        };
    }

    [Fact]
    public async Task RejectsAPlanThePlanListDoesNotOffer_BeforeAnyClaimOrWrite()
    {
        var (service, db, handler) = Create(Router());

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync("u1", "a@b.c", "not-offered", default));

        Assert.Equal(BillingFailure.InvalidPlan, ex.Failure);
        Assert.Empty(db.SubscriptionClaims);
        Assert.Equal(0, handler.Count(HttpMethod.Post, ""));
    }

    [Fact]
    public async Task SecondSubscribeByTheSameUser_ReturnsExisting_AndDoesNotWriteTwice()
    {
        var (service, db, handler) = Create(Router(onFind: _ => Json(HttpStatusCode.OK, SubscriptionJson)));

        var first = await service.SubscribeAsync("u1", "a@b.c", "eshop-pro", default);
        var second = await service.SubscribeAsync("u1", "a@b.c", "eshop-pro", default);

        Assert.Equal(SubscribeOutcome.Created, first.Outcome);
        Assert.Equal("22", first.Subscription!.Id);
        Assert.Equal(29900, first.Subscription.PriceInCents);
        Assert.Equal(SubscribeOutcome.AlreadySubscribed, second.Outcome);
        Assert.Equal(1, handler.Count(HttpMethod.Post, "customers"));
        Assert.Equal(1, handler.Count(HttpMethod.Post, "subscriptions"));
        Assert.Equal(SubscriptionClaimState.Completed, (await db.SubscriptionClaims.SingleAsync()).State);
    }

    [Fact]
    public async Task AClaimInFlight_RefusesTheSecondCaller_BeforeItReachesMaxio()
    {
        var (service, db, handler) = Create(Router());
        db.SubscriptionClaims.Add(new SubscriptionClaim("u1", "eshop-pro", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync("u1", "a@b.c", "eshop-pro", default));

        Assert.Equal(BillingFailure.InProgress, ex.Failure);
        Assert.Equal(0, handler.Count(HttpMethod.Post, ""));
    }

    [Fact]
    public async Task ConnectionFailureOnSubscriptionWrite_IsSettledByReReading_AndNotResent()
    {
        var found = false;
        var (service, db, handler) = Create(Router(
            onSubscriptionPost: _ => { found = true; throw new HttpRequestException("connection reset"); },
            onFind: _ => found ? Json(HttpStatusCode.OK, SubscriptionJson) : Json(HttpStatusCode.NotFound, "{}")));

        var result = await service.SubscribeAsync("u1", "a@b.c", "eshop-pro", default);

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal("22", result.Subscription!.Id);
        Assert.Equal(1, handler.Count(HttpMethod.Post, "subscriptions"));
        Assert.Equal(SubscriptionClaimState.Completed, (await db.SubscriptionClaims.SingleAsync()).State);
    }

    [Fact]
    public async Task ConnectionFailureThatReReadCannotConfirm_LeavesTheClaimUnknown_AndTheNextSubscribeSettlesIt()
    {
        var landed = false;
        var (service, db, handler) = Create(Router(
            onSubscriptionPost: _ => throw new HttpRequestException("connection reset"),
            onFind: _ => landed ? Json(HttpStatusCode.OK, SubscriptionJson) : Json(HttpStatusCode.NotFound, "{}")));

        var first = await service.SubscribeAsync("u1", "a@b.c", "eshop-pro", default);

        Assert.Equal(SubscribeOutcome.PendingConfirmation, first.Outcome);
        Assert.Null(first.Subscription);
        Assert.Equal(SubscriptionClaimState.Unknown, (await db.SubscriptionClaims.SingleAsync()).State);
        Assert.Equal(1, handler.Count(HttpMethod.Post, "subscriptions"));

        landed = true; // the write had in fact landed at Maxio
        var second = await service.SubscribeAsync("u1", "a@b.c", "eshop-pro", default);

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, second.Outcome);
        Assert.Equal(1, handler.Count(HttpMethod.Post, "subscriptions")); // not created a second time
    }

    [Fact]
    public async Task ProviderRefusal_ReleasesTheClaimSoACorrectedRetryCanProceed()
    {
        var (service, db, _) = Create(Router(onSubscriptionPost: _ =>
            Json(HttpStatusCode.UnprocessableEntity, """{"errors":["No payment method"]}""")));

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync("u1", "a@b.c", "eshop-pro", default));

        Assert.Equal(BillingFailure.Rejected, ex.Failure);
        Assert.Equal(422, ex.ProviderStatus);
        Assert.Empty(db.SubscriptionClaims);
    }
}
