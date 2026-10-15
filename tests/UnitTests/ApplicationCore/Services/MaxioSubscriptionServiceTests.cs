using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class MaxioSubscriptionServiceTests
{
    private const string USER = "shopper@example.com";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _responder;
        public List<string> Calls { get; } = new();

        public StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // Read the body now: the SDK disposes request content after the attempt.
            var body = request.Content?.ReadAsStringAsync(ct).Result;
            Calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            var response = _responder(request, body);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string PLANS = """[{"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""";
    private const string CUSTOMER = """{"customer":{"id":11,"reference":"eshop-user:shopper@example.com"}}""";
    private const string SUBSCRIPTION = """{"subscription":{"id":22,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2030-01-01T00:00:00Z","product":{"handle":"eshop-pro","name":"Pro Plan"}}}""";

    private static HttpResponseMessage Happy(HttpRequestMessage r, string? _) => r.RequestUri!.AbsolutePath switch
    {
        var p when p.EndsWith("/products.json") => Json(HttpStatusCode.OK, PLANS),
        "/customers/lookup.json" => Json(HttpStatusCode.NotFound, "{}"),
        "/customers.json" => Json(HttpStatusCode.Created, CUSTOMER),
        "/subscriptions/lookup.json" => Json(HttpStatusCode.NotFound, "{}"),
        "/subscriptions.json" => Json(HttpStatusCode.Created, SUBSCRIPTION),
        "/subscriptions/22.json" => Json(HttpStatusCode.OK, SUBSCRIPTION),
        _ => Json(HttpStatusCode.NotFound, "{}"),
    };

    private sealed class Harness
    {
        private readonly MaxioAdvancedBillingClient _client;
        private readonly string _store = Guid.NewGuid().ToString();
        public StubHandler Handler { get; }

        public Harness(Func<HttpRequestMessage, string?, HttpResponseMessage> responder)
        {
            Handler = new StubHandler(responder);
            _client = new MaxioAdvancedBillingClient(new HttpClient(Handler), new MaxioAdvancedBillingClientOptions
            {
                // Retries are the SDK's own concern; keep unit tests to one attempt.
                Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Disabled(),
            });
        }

        /// <summary>A new DbContext per call, like one scoped context per HTTP request.</summary>
        public CatalogContext Db() => new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_store).Options);

        public MaxioSubscriptionService Service() => new(_client, Db(),
            Options.Create(new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "family" }),
            NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static SubscribeCommand Command(string plan = "eshop-pro") => new(USER, plan, null, null);

    [Fact]
    public async Task Subscribe_creates_customer_then_subscription_and_records_both()
    {
        var h = new Harness(Happy);

        var result = await h.Service().SubscribeAsync(Command(), CancellationToken.None);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(22, result.Subscription.SubscriptionId);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.NotNull(result.Subscription.NextBillingAt);
        Assert.Equal(11, (await h.Db().MaxioCustomerLinks.SingleAsync()).MaxioId);
        Assert.Equal(ClaimState.Active, (await h.Db().UserSubscriptions.SingleAsync()).State);
        Assert.Equal(1, h.Handler.Calls.Count(c => c == "POST /customers.json"));
        Assert.Equal(1, h.Handler.Calls.Count(c => c == "POST /subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_twice_does_not_write_to_maxio_again()
    {
        var h = new Harness(Happy);
        await h.Service().SubscribeAsync(Command(), CancellationToken.None);

        var second = await h.Service().SubscribeAsync(Command(), CancellationToken.None);

        Assert.True(second.AlreadySubscribed);
        Assert.Equal(1, h.Handler.Calls.Count(c => c.StartsWith("POST")  && c.EndsWith("/customers.json")));
        Assert.Equal(1, h.Handler.Calls.Count(c => c == "POST /subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_rejects_a_plan_the_family_does_not_offer_before_any_write()
    {
        var h = new Harness(Happy);

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.Service().SubscribeAsync(Command("not-a-plan"), CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
        Assert.DoesNotContain(h.Handler.Calls, c => c.StartsWith("POST"));
    }

    [Fact]
    public async Task Subscribe_validation_rejection_releases_the_claim_and_returns_422()
    {
        var h = new Harness((r, b) => r.RequestUri!.AbsolutePath == "/subscriptions.json"
            ? Json(HttpStatusCode.UnprocessableEntity, """{"errors":["No payment method"]}""")
            : Happy(r, b));

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.Service().SubscribeAsync(Command(), CancellationToken.None));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("No payment method", ex.Message);
        Assert.Empty(await h.Db().UserSubscriptions.ToListAsync()); // released: the user can try again
    }

    [Fact]
    public async Task Subscribe_connection_failure_on_write_is_settled_by_reconciling_on_the_reference()
    {
        var created = false;
        var h = new Harness((r, b) =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path == "/subscriptions.json") { created = true; throw new HttpRequestException("connection reset"); }
            if (path == "/subscriptions/lookup.json" && created) return Json(HttpStatusCode.OK, SUBSCRIPTION); // the write had landed
            return Happy(r, b);
        });

        var result = await h.Service().SubscribeAsync(Command(), CancellationToken.None);

        Assert.Equal(22, result.Subscription.SubscriptionId);
        Assert.Equal(22, (await h.Db().UserSubscriptions.SingleAsync()).MaxioId);
    }

    [Fact]
    public async Task Subscribe_connection_failure_with_nothing_found_leaves_an_unknown_claim_and_never_duplicates()
    {
        var h = new Harness((r, b) =>
            r.RequestUri!.AbsolutePath == "/subscriptions.json" ? throw new HttpRequestException("connection reset") : Happy(r, b));

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.Service().SubscribeAsync(Command(), CancellationToken.None));
        var again = await Assert.ThrowsAsync<SubscriptionException>(() => h.Service().SubscribeAsync(Command(), CancellationToken.None));

        Assert.Equal(503, ex.StatusCode);
        Assert.Equal(ClaimState.Unknown, (await h.Db().UserSubscriptions.SingleAsync()).State);
        Assert.Equal(409, again.StatusCode); // claim still held; no second write attempted
        Assert.Equal(1, h.Handler.Calls.Count(c => c == "POST /subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_connection_failure_creating_the_customer_is_settled_by_reconciling_on_the_reference()
    {
        var created = false;
        var h = new Harness((r, b) =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path == "/customers.json") { created = true; throw new HttpRequestException("connection reset"); }
            if (path == "/customers/lookup.json" && created) return Json(HttpStatusCode.OK, CUSTOMER); // the write had landed
            return Happy(r, b);
        });

        var result = await h.Service().SubscribeAsync(Command(), CancellationToken.None);

        Assert.Equal(22, result.Subscription.SubscriptionId);
        Assert.Equal(11, (await h.Db().MaxioCustomerLinks.SingleAsync()).MaxioId);
        Assert.Equal(1, h.Handler.Calls.Count(c => c == "POST /customers.json"));
    }

    [Fact]
    public async Task Concurrent_claims_on_the_same_user_and_plan_are_refused_by_the_store()
    {
        var h = new Harness(Happy);
        // Another request already holds the claim for this user and plan, with no recorded result.
        await using (var db = h.Db())
        {
            db.UserSubscriptions.Add(new UserSubscription { UserName = USER, PlanHandle = "eshop-pro", Reference = $"eshop-sub:{USER}:eshop-pro" });
            db.MaxioCustomerLinks.Add(new MaxioCustomerLink { UserName = USER, Reference = $"eshop-user:{USER}", MaxioId = 11, State = ClaimState.Active });
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.Service().SubscribeAsync(Command(), CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.DoesNotContain(h.Handler.Calls, c => c == "POST /subscriptions.json");
    }

    [Fact]
    public async Task Plans_are_listed_from_the_configured_family_and_archived_ones_are_dropped()
    {
        var h = new Harness((r, _) => Json(HttpStatusCode.OK,
            """[{"product":{"handle":"a","name":"A","price_in_cents":100}},{"product":{"handle":"b","name":"B","archived_at":"2020-01-01T00:00:00Z"}}]"""));

        var plans = await h.Service().ListPlansAsync(CancellationToken.None);

        Assert.Equal(["a"], plans.Plans.Select(p => p.Handle));
        Assert.False(plans.Truncated);
        Assert.Contains("handle:family", h.Handler.Calls.Single().Replace("%3A", ":"));
    }

    [Fact]
    public async Task MySubscriptions_is_empty_for_a_user_without_a_billing_customer()
    {
        var h = new Harness(Happy);

        Assert.Empty(await h.Service().ListMineAsync(USER, CancellationToken.None));
        Assert.Empty(h.Handler.Calls);
    }

    [Theory]
    [InlineData(null, "s", "f")]
    [InlineData("k", " ", "f")]
    [InlineData("k", "s", "")]
    public void Settings_with_a_blank_required_value_fail_validation_naming_the_key(string? key, string sub, string family)
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings { ApiKey = key, Subdomain = sub, ProductFamilyHandle = family });

        Assert.True(result.Failed);
        Assert.Contains("Maxio:", result.FailureMessage);
    }
}
