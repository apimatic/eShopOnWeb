using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Subscriptions;

/// <summary>In-memory store whose TryClaim refuses a second row for the same user, like the primary key does.</summary>
public class FakeEnrollmentStore : ISubscriptionEnrollmentStore
{
    public Dictionary<string, SubscriptionEnrollment> Rows { get; } = new();
    public int ClaimAttempts { get; private set; }

    public Task<EnrollmentClaimResult> TryClaimAsync(SubscriptionEnrollment candidate, CancellationToken cancellationToken = default)
    {
        ClaimAttempts++;
        if (Rows.TryGetValue(candidate.UserId, out var existing))
            return Task.FromResult(new EnrollmentClaimResult(false, existing));
        Rows[candidate.UserId] = candidate;
        return Task.FromResult(new EnrollmentClaimResult(true, candidate));
    }

    public Task<SubscriptionEnrollment?> GetAsync(string userId, CancellationToken cancellationToken = default)
        => Task.FromResult(Rows.GetValueOrDefault(userId));

    public Task SaveAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ReleaseAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        Rows.Remove(enrollment.UserId);
        return Task.CompletedTask;
    }
}

public class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content?.ReadAsStringAsync().Result);
        var response = _responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }

    public int Count(HttpMethod method, string pathPart)
        => Requests.Count(r => r.Method == method && r.RequestUri!.AbsolutePath.Contains(pathPart));
}

public class SubscriptionServiceTests
{
    // Obviously fake values: nothing here is a real key, site or handle.
    private static readonly MaxioSettings FakeSettings = new()
    {
        ApiKey = "test-dummy-api-key",
        Subdomain = "test-dummy-site",
        ProductFamilyHandle = "test-dummy-family",
        BaseUrl = null
    };

    private const string PlansJson =
        """[{"product":{"id":1,"handle":"plan-a","name":"Plan A","price_in_cents":1500,"interval":1,"interval_unit":"month"}},{"product":{"id":2,"handle":"plan-old","name":"Old","archived_at":"2020-01-01T00:00:00Z"}}]""";
    private const string CustomerJson = """{"customer":{"id":11,"reference":"x"}}""";
    private const string SubscriptionJson =
        """{"subscription":{"id":22,"state":"active","next_assessment_at":"2030-02-01T00:00:00Z","current_period_ends_at":"2030-02-01T00:00:00Z","product":{"id":1,"handle":"plan-a","name":"Plan A","price_in_cents":1500,"interval":1,"interval_unit":"month"},"customer":{"id":11}}}""";

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Empty(HttpStatusCode status) => new(status) { Content = new StringContent("") };

    /// <summary>Responder with per-route overrides; defaults describe a healthy Maxio with no existing customer or subscription.</summary>
    private class Maxio
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Plans = _ => Json(HttpStatusCode.OK, PlansJson);
        public Func<HttpRequestMessage, HttpResponseMessage> CustomerLookup = _ => Empty(HttpStatusCode.NotFound);
        public Func<HttpRequestMessage, HttpResponseMessage> CustomerCreate = _ => Json(HttpStatusCode.Created, CustomerJson);
        public Func<HttpRequestMessage, HttpResponseMessage> SubscriptionCreate = _ => Json(HttpStatusCode.Created, SubscriptionJson);
        public Func<HttpRequestMessage, HttpResponseMessage> SubscriptionFind = _ => Empty(HttpStatusCode.NotFound);
        public Func<HttpRequestMessage, HttpResponseMessage> SubscriptionRead = _ => Json(HttpStatusCode.OK, SubscriptionJson);

        public HttpResponseMessage Route(HttpRequestMessage r)
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path.Contains("/products")) return Plans(r);
            if (path.Contains("/customers/lookup")) return CustomerLookup(r);
            if (path.EndsWith("/customers.json")) return CustomerCreate(r);
            if (path.Contains("/subscriptions/lookup")) return SubscriptionFind(r);
            if (path.EndsWith("/subscriptions.json")) return SubscriptionCreate(r);
            if (path.Contains("/subscriptions/")) return SubscriptionRead(r);
            return Empty(HttpStatusCode.NotFound);
        }
    }

    private class Harness
    {
        public Maxio Maxio { get; } = new();
        public FakeEnrollmentStore Store { get; } = new();
        public StubHandler Handler { get; }
        public SubscriptionService Service { get; }

        public Harness(Action<Maxio>? configure = null, MaxioSettings? settings = null)
        {
            configure?.Invoke(Maxio);
            settings ??= FakeSettings;
            Handler = new StubHandler(Maxio.Route);
            var options = MaxioServiceCollectionExtensions.BuildClientOptions(settings, null);
            options.Retry = RetryOptions.Disabled();
            var client = new MaxioAdvancedBillingClient(new HttpClient(Handler), options);
            var gateway = new MaxioBillingGateway(client, Options.Create(settings), NullLogger<MaxioBillingGateway>.Instance);
            Service = new SubscriptionService(gateway, Store, NullLogger<SubscriptionService>.Instance);
        }

        public SubscribeCommand Command(string? plan = "plan-a", string user = "user-1")
            => new(user, "shopper@example.test", "shopper@example.test", plan);

        public int Posts => Handler.Requests.Count(r => r.Method == HttpMethod.Post);
    }

    // ------------------------------------------------------------------ plans

    [Fact]
    public async Task ListPlans_returns_non_archived_plans_of_the_configured_family()
    {
        var h = new Harness();
        var listing = await h.Service.ListPlansAsync(default);

        var plan = Assert.Single(listing.Plans);
        Assert.Equal("plan-a", plan.Handle);
        Assert.Equal(1500, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.True(listing.IsComplete);
        Assert.Contains("handle:test-dummy-family", h.Handler.Requests[0].RequestUri!.AbsolutePath.Replace("%3A", ":"));
    }

    [Fact]
    public async Task ListPlans_reports_incomplete_when_page_cap_is_hit()
    {
        var full = "[" + string.Join(",", Enumerable.Range(0, MaxioBillingGateway.PlanPageSize)
            .Select(i => $"{{\"product\":{{\"id\":{i},\"handle\":\"p{i}\"}}}}")) + "]";
        var h = new Harness(m => m.Plans = _ => Json(HttpStatusCode.OK, full));

        var listing = await h.Service.ListPlansAsync(default);

        Assert.False(listing.IsComplete);
        Assert.Equal(MaxioBillingGateway.MaxPlanPages, h.Handler.Requests.Count);
    }

    // -------------------------------------------------------------- subscribe

    [Fact]
    public async Task Subscribe_creates_customer_then_subscription_and_returns_plan_price_state_and_next_billing_date()
    {
        var h = new Harness();

        var view = await h.Service.SubscribeAsync(h.Command(), default);

        Assert.Equal("plan-a", view.PlanHandle);
        Assert.Equal(1500, view.PriceInCents);
        Assert.Equal("active", view.State);
        Assert.Equal(new DateTimeOffset(2030, 2, 1, 0, 0, 0, TimeSpan.Zero), view.NextBillingDate);

        var custBody = h.Handler.Bodies[h.Handler.Requests.FindIndex(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/customers.json"))];
        Assert.Contains("\"reference\":\"" + SubscriptionService.CustomerReferencePrefix + "user-1\"", custBody);
        var subBody = h.Handler.Bodies[h.Handler.Requests.FindIndex(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json"))];
        Assert.Contains("\"product_handle\":\"plan-a\"", subBody);
        Assert.Contains("\"customer_id\":11", subBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subBody);

        var row = h.Store.Rows["user-1"];
        Assert.Equal(EnrollmentStatus.Active, row.Status);
        Assert.Equal(22, row.MaxioSubscriptionId);
        Assert.Equal(11, row.MaxioCustomerId);
    }

    [Fact]
    public async Task Subscribe_twice_makes_one_customer_and_one_subscription()
    {
        var h = new Harness();

        var first = await h.Service.SubscribeAsync(h.Command(), default);
        var second = await h.Service.SubscribeAsync(h.Command(), default);

        Assert.Equal(first.SubscriptionId, second.SubscriptionId);
        Assert.Equal(1, h.Handler.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, h.Handler.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(2, h.Store.ClaimAttempts);
    }

    [Fact]
    public async Task Subscribe_with_a_claim_already_held_is_refused_without_any_maxio_write()
    {
        var h = new Harness();
        // another request holds a fresh claim
        h.Store.Rows["user-1"] = new SubscriptionEnrollment("user-1", "plan-a", "c", "s", DateTimeOffset.UtcNow);

        var ex = await Assert.ThrowsAsync<SubscriptionFlowException>(() => h.Service.SubscribeAsync(h.Command(), default));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(0, h.Posts);
        Assert.Single(h.Store.Rows);
    }

    [Fact]
    public async Task Subscribe_to_a_second_distinct_plan_is_refused_with_409_and_no_write()
    {
        var h = new Harness(m => m.Plans = _ => Json(HttpStatusCode.OK,
            """[{"product":{"id":1,"handle":"plan-a"}},{"product":{"id":3,"handle":"plan-b"}}]"""));
        await h.Service.SubscribeAsync(h.Command("plan-a"), default);
        var postsBefore = h.Posts;

        var ex = await Assert.ThrowsAsync<SubscriptionFlowException>(() => h.Service.SubscribeAsync(h.Command("plan-b"), default));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(postsBefore, h.Posts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Subscribe_requires_a_plan_handle(string? handle)
    {
        var h = new Harness();
        var ex = await Assert.ThrowsAsync<SubscriptionFlowException>(() => h.Service.SubscribeAsync(h.Command(handle), default));
        Assert.Equal(400, ex.StatusCode);
        Assert.Empty(h.Handler.Requests);
        Assert.Equal(0, h.Store.ClaimAttempts);
    }

    [Fact]
    public async Task Subscribe_rejects_a_plan_the_family_does_not_offer_before_claiming_or_writing()
    {
        var h = new Harness();
        var ex = await Assert.ThrowsAsync<SubscriptionFlowException>(() => h.Service.SubscribeAsync(h.Command("plan-old"), default));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(0, h.Store.ClaimAttempts);
        Assert.Equal(0, h.Posts);
    }

    [Fact]
    public async Task Subscribe_with_an_incomplete_plan_listing_and_unseen_handle_is_unavailable_not_unknown_plan()
    {
        var full = "[" + string.Join(",", Enumerable.Range(0, MaxioBillingGateway.PlanPageSize)
            .Select(i => $"{{\"product\":{{\"id\":{i},\"handle\":\"p{i}\"}}}}")) + "]";
        var h = new Harness(m => m.Plans = _ => Json(HttpStatusCode.OK, full));

        var ex = await Assert.ThrowsAsync<SubscriptionFlowException>(() => h.Service.SubscribeAsync(h.Command("not-in-first-pages"), default));

        Assert.Equal(503, ex.StatusCode);
        Assert.Equal(0, h.Store.ClaimAttempts);
    }

    [Fact]
    public async Task Subscribe_reuses_an_existing_maxio_customer_found_by_reference()
    {
        var h = new Harness(m => m.CustomerLookup = _ => Json(HttpStatusCode.OK, """{"customer":{"id":77}}"""));

        await h.Service.SubscribeAsync(h.Command(), default);

        Assert.Equal(0, h.Handler.Count(HttpMethod.Post, "/customers.json"));
        Assert.Contains("\"customer_id\":77", h.Handler.Bodies[h.Handler.Requests.FindIndex(r => r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json"))]);
    }

    [Fact]
    public async Task Subscribe_releases_the_claim_when_maxio_refuses_the_subscription()
    {
        var h = new Harness(m => m.SubscriptionCreate = _ => Json(HttpStatusCode.UnprocessableEntity, """{"errors":["bad"]}"""));

        var ex = await Assert.ThrowsAsync<SubscriptionFlowException>(() => h.Service.SubscribeAsync(h.Command(), default));

        Assert.Equal(422, ex.StatusCode);
        Assert.DoesNotContain("bad", ex.Message);
        Assert.Empty(h.Store.Rows);
    }

    // -------------------------------------------------- unknown outcomes

    [Fact]
    public async Task CreateSubscription_connection_failure_is_settled_by_FindSubscription_and_adopts_the_subscription()
    {
        var h = new Harness(m =>
        {
            m.SubscriptionCreate = _ => throw new HttpRequestException("connection reset");
            m.SubscriptionFind = _ => Json(HttpStatusCode.OK, SubscriptionJson);
        });

        var view = await h.Service.SubscribeAsync(h.Command(), default);

        Assert.Equal(22, view.SubscriptionId);
        Assert.Equal(EnrollmentStatus.Active, h.Store.Rows["user-1"].Status);
        Assert.Equal(1, h.Handler.Count(HttpMethod.Post, "/subscriptions.json")); // never re-sent
        Assert.Equal(1, h.Handler.Count(HttpMethod.Get, "/subscriptions/lookup"));
        Assert.Contains(SubscriptionService.SubscriptionReferencePrefix + "user-1", h.Handler.Requests.Last().RequestUri!.Query);
    }

    [Fact]
    public async Task CreateSubscription_connection_failure_with_nothing_found_releases_the_claim()
    {
        var h = new Harness(m => m.SubscriptionCreate = _ => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<SubscriptionFlowException>(() => h.Service.SubscribeAsync(h.Command(), default));

        Assert.Equal(503, ex.StatusCode);
        Assert.Empty(h.Store.Rows);
    }

    [Fact]
    public async Task CreateSubscription_connection_failure_with_failed_reread_leaves_Unknown_and_my_subscriptions_settles_it()
    {
        var findCalls = 0;
        var h = new Harness(m =>
        {
            m.SubscriptionCreate = _ => throw new HttpRequestException("connection reset");
            m.SubscriptionFind = _ => ++findCalls == 1
                ? throw new HttpRequestException("still down")
                : Json(HttpStatusCode.OK, SubscriptionJson);
        });

        var ex = await Assert.ThrowsAsync<SubscriptionFlowException>(() => h.Service.SubscribeAsync(h.Command(), default));
        Assert.Equal(504, ex.StatusCode);
        Assert.Equal(EnrollmentStatus.Unknown, h.Store.Rows["user-1"].Status);

        var mine = await h.Service.GetMySubscriptionsAsync("user-1", default);

        var view = Assert.Single(mine);
        Assert.Equal(22, view.SubscriptionId);
        Assert.Equal(EnrollmentStatus.Active, h.Store.Rows["user-1"].Status);
        Assert.Equal(1, h.Handler.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task A_repeat_subscribe_on_an_Unknown_row_re_reads_instead_of_writing_again()
    {
        var h = new Harness(m => m.SubscriptionFind = _ => Json(HttpStatusCode.OK, SubscriptionJson));
        var row = new SubscriptionEnrollment("user-1", "plan-a", SubscriptionService.CustomerReferencePrefix + "user-1",
            SubscriptionService.SubscriptionReferencePrefix + "user-1", DateTimeOffset.UtcNow);
        row.MarkUnknown(DateTimeOffset.UtcNow);
        h.Store.Rows["user-1"] = row;

        var view = await h.Service.SubscribeAsync(h.Command(), default);

        Assert.Equal(22, view.SubscriptionId);
        Assert.Equal(0, h.Posts);
    }

    [Fact]
    public async Task CreateCustomer_connection_failure_is_settled_by_ReadCustomerByReference_and_flow_continues()
    {
        var lookups = 0;
        var h = new Harness(m =>
        {
            m.CustomerLookup = _ => ++lookups == 1 ? Empty(HttpStatusCode.NotFound) : Json(HttpStatusCode.OK, """{"customer":{"id":55}}""");
            m.CustomerCreate = _ => throw new HttpRequestException("connection reset");
        });

        var view = await h.Service.SubscribeAsync(h.Command(), default);

        Assert.Equal(22, view.SubscriptionId);
        Assert.Equal(2, lookups);
        Assert.Equal(1, h.Handler.Count(HttpMethod.Post, "/customers.json")); // never re-sent
        Assert.Contains("\"customer_id\":55", h.Handler.Bodies[h.Handler.Requests.FindIndex(r => r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json"))]);
    }

    [Fact]
    public async Task CreateCustomer_connection_failure_with_no_customer_found_releases_the_claim_and_creates_no_subscription()
    {
        var h = new Harness(m => m.CustomerCreate = _ => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<SubscriptionFlowException>(() => h.Service.SubscribeAsync(h.Command(), default));

        Assert.Equal(503, ex.StatusCode);
        Assert.Empty(h.Store.Rows);
        Assert.Equal(0, h.Handler.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task A_2xx_customer_response_that_cannot_be_read_is_an_unknown_outcome_not_a_failure()
    {
        var lookups = 0;
        var h = new Harness(m =>
        {
            m.CustomerLookup = _ => ++lookups == 1 ? Empty(HttpStatusCode.NotFound) : Json(HttpStatusCode.OK, """{"customer":{"id":9}}""");
            m.CustomerCreate = _ => Json(HttpStatusCode.Created, """{"customer":"not-an-object"}""");
        });

        var view = await h.Service.SubscribeAsync(h.Command(), default);

        Assert.Equal(22, view.SubscriptionId);
        Assert.Equal(1, h.Handler.Count(HttpMethod.Post, "/customers.json"));
    }

    // ------------------------------------------------------ my subscriptions

    [Fact]
    public async Task MySubscriptions_returns_live_values_for_the_callers_own_row_only()
    {
        var h = new Harness();
        await h.Service.SubscribeAsync(h.Command(user: "user-1"), default);

        var mine = await h.Service.GetMySubscriptionsAsync("user-1", default);
        var others = await h.Service.GetMySubscriptionsAsync("user-2", default);

        var view = Assert.Single(mine);
        Assert.True(view.IsLive);
        Assert.Equal("active", view.State);
        Assert.Empty(others);
        Assert.Contains("/subscriptions/22", h.Handler.Requests.Last().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task MySubscriptions_falls_back_to_the_stored_copy_when_maxio_is_down()
    {
        var down = false;
        var h = new Harness(m => m.SubscriptionRead = _ => down ? Empty(HttpStatusCode.ServiceUnavailable) : Json(HttpStatusCode.OK, SubscriptionJson));
        await h.Service.SubscribeAsync(h.Command(), default);
        down = true;

        var view = Assert.Single(await h.Service.GetMySubscriptionsAsync("user-1", default));

        Assert.False(view.IsLive);
        Assert.Equal("plan-a", view.PlanHandle);
        Assert.Equal(22, view.SubscriptionId);
    }

    // ---------------------------------------------------- configuration / client

    [Fact]
    public async Task BaseUrl_when_set_is_used_verbatim_otherwise_the_address_is_derived_from_the_subdomain()
    {
        var withBase = new Harness(settings: new MaxioSettings
        {
            ApiKey = "test-dummy-api-key", Subdomain = "test-dummy-site", ProductFamilyHandle = "test-dummy-family", BaseUrl = "http://base.test.invalid:1234"
        });
        await withBase.Service.ListPlansAsync(default);
        Assert.Equal("base.test.invalid", withBase.Handler.Requests[0].RequestUri!.Host);
        Assert.Equal(1234, withBase.Handler.Requests[0].RequestUri!.Port);

        var derived = new Harness();
        await derived.Service.ListPlansAsync(default);
        Assert.Equal("test-dummy-site.chargify.com", derived.Handler.Requests[0].RequestUri!.Host);
        Assert.StartsWith("Basic ", derived.Handler.Requests[0].Headers.Authorization!.ToString());
    }

    [Fact]
    public void Settings_validation_names_each_blank_key_and_never_a_value()
    {
        var validator = new MaxioSettingsValidator();

        var result = validator.Validate(null, new MaxioSettings { ApiKey = " ", Subdomain = "", ProductFamilyHandle = null, BaseUrl = "ftp://x" });

        Assert.True(result.Failed);
        var text = string.Join("|", result.Failures!);
        Assert.Contains("Maxio:ApiKey", text);
        Assert.Contains("Maxio:Subdomain", text);
        Assert.Contains("Maxio:ProductFamilyHandle", text);
        Assert.Contains("Maxio:BaseUrl", text);
        Assert.DoesNotContain("ftp://x", text);

        Assert.True(validator.Validate(null, FakeSettings).Succeeded);
    }
}
