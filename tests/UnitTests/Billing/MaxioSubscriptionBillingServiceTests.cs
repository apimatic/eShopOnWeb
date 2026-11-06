using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FAMILY = "fam";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode, string)> _responder;
        public List<(HttpMethod Method, string Path)> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            var (status, json) = _responder(request);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static MaxioSubscriptionBillingService Build(StubHandler handler)
    {
        var settings = new MaxioSettings { ApiKey = "test-key", ProductFamilyHandle = FAMILY, BaseUrl = "http://maxio.test" };
        var client = MaxioClientFactory.Create(new HttpClient(handler), settings);
        return new MaxioSubscriptionBillingService(() => client, FAMILY, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private const string PRODUCT = """{"product":{"id":1,"handle":"pro","name":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month","product_family":{"id":9,"handle":"fam"}}}""";
    private const string CUSTOMER = """{"customer":{"id":42,"reference":"a@b.c"}}""";
    private const string SUB = """{"subscription":{"id":7,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2030-01-01T00:00:00Z","product":{"id":1,"handle":"pro","name":"Pro","price_in_cents":29900}}}""";

    [Fact]
    public async Task Subscribe_CreatesCustomerThenSubscription_Once()
    {
        var created = false;
        var handler = new StubHandler(r =>
        {
            var p = r.RequestUri!.AbsolutePath;
            if (p.Contains("products/handle")) return (HttpStatusCode.OK, PRODUCT);
            if (p.Contains("customers/lookup")) return created ? (HttpStatusCode.OK, CUSTOMER) : (HttpStatusCode.NotFound, "{}");
            if (r.Method == HttpMethod.Post && p.EndsWith("/customers.json")) { created = true; return (HttpStatusCode.Created, CUSTOMER); }
            if (p.Contains("customers/42/subscriptions")) return (HttpStatusCode.OK, "[]");
            if (r.Method == HttpMethod.Post && p.EndsWith("/subscriptions.json")) return (HttpStatusCode.Created, SUB);
            return (HttpStatusCode.NotFound, "{}");
        });

        var result = await Build(handler).SubscribeAsync("a@b.c", "pro");

        Assert.True(result.Created);
        Assert.Equal(299m, result.Subscription.Price);
        Assert.Equal("active", result.Subscription.State);
        Assert.NotNull(result.Subscription.NextBillingDate);
        Assert.Equal(1, handler.Requests.Count(x => x.Method == HttpMethod.Post && x.Path.EndsWith("/subscriptions.json")));
        Assert.Equal(1, handler.Requests.Count(x => x.Method == HttpMethod.Post && x.Path.EndsWith("/customers.json")));
    }

    [Fact]
    public async Task Subscribe_WhenLiveSubscriptionExists_DoesNotPost()
    {
        var handler = new StubHandler(r =>
        {
            var p = r.RequestUri!.AbsolutePath;
            if (p.Contains("products/handle")) return (HttpStatusCode.OK, PRODUCT);
            if (p.Contains("customers/lookup")) return (HttpStatusCode.OK, CUSTOMER);
            if (p.Contains("customers/42/subscriptions")) return (HttpStatusCode.OK, "[" + SUB + "]");
            return (HttpStatusCode.InternalServerError, "{}");
        });

        var result = await Build(handler).SubscribeAsync("a@b.c", "pro");

        Assert.False(result.Created);
        Assert.Equal(7, result.Subscription.SubscriptionId);
        Assert.DoesNotContain(handler.Requests, x => x.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Subscribe_ProviderValidationError_Maps422()
    {
        var handler = new StubHandler(r =>
        {
            var p = r.RequestUri!.AbsolutePath;
            if (p.Contains("products/handle")) return (HttpStatusCode.OK, PRODUCT);
            if (p.Contains("customers/lookup")) return (HttpStatusCode.OK, CUSTOMER);
            if (p.Contains("customers/42/subscriptions")) return (HttpStatusCode.OK, "[]");
            return (HttpStatusCode.UnprocessableEntity, """{"errors":["no payment method"]}""");
        });

        var ex = await Assert.ThrowsAsync<BillingException>(() => Build(handler).SubscribeAsync("a@b.c", "pro"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Contains("no payment method", ex.Message);
    }

    [Fact]
    public async Task Subscribe_PlanOutsideFamily_Is404()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, PRODUCT.Replace("\"fam\"", "\"other\"")));

        var ex = await Assert.ThrowsAsync<BillingException>(() => Build(handler).SubscribeAsync("a@b.c", "pro"));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task ListSubscriptions_UnknownCustomer_IsEmptyAndCreatesNothing()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.NotFound, "{}"));

        var subs = await Build(handler).ListSubscriptionsAsync("a@b.c");

        Assert.Empty(subs);
        Assert.DoesNotContain(handler.Requests, x => x.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ListPlans_MapsCentsToDecimal()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, "[" + PRODUCT + "]"));

        var plans = await Build(handler).ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("pro", plan.Handle);
        Assert.Equal(299m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task UpstreamServerError_Is502_NotLeakingDetail()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.Unauthorized, "secret detail"));

        var ex = await Assert.ThrowsAsync<BillingException>(() => Build(handler).ListPlansAsync());

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.DoesNotContain("secret", ex.Message);
    }
}
