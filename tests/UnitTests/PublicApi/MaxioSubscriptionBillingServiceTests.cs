using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi;

/// <summary>
/// Covers the Maxio billing boundary against a stubbed HTTP pipeline — no live traffic.
/// The seam is the HttpClient the SDK client wraps (write guard -> logging -> stub),
/// the same chain the production registration builds.
/// </summary>
public class MaxioSubscriptionBillingServiceTests
{
    private const string Email = "demouser@microsoft.com";
    private const string BaseUrl = "https://maxio.test";

    private const string CustomerJson =
        """{"customer":{"id":4001,"first_name":"Demo","last_name":"User","email":"demouser@microsoft.com","reference":"eshoponweb-hash"}}""";

    private const string FamiliesJson =
        """[{"product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop Subscribe"}}]""";

    private static (MaxioSubscriptionBillingService Service, StubHandler Stub) BuildService(StubHandler stub)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Retry = RetryOptions.Default() with { MaxRetries = 1, Timeout = TimeSpan.FromSeconds(5) },
        };
        clientOptions.Server.Production.Us.BaseUrl = BaseUrl;

        var logging = new MaxioRequestLoggingHandler(NullLogger<MaxioRequestLoggingHandler>.Instance);
        var guard = new MaxioWriteGuardHandler { InnerHandler = logging };
        logging.InnerHandler = stub;

        var client = new MaxioAdvancedBillingClient(new HttpClient(guard), clientOptions);
        var service = new MaxioSubscriptionBillingService(
            client,
            Options.Create(new MaxioOptions
            {
                ApiKey = "test-key",
                Subdomain = "test-site",
                ProductFamilyHandle = "eshop-subscribe",
            }),
            NullLogger<MaxioSubscriptionBillingService>.Instance);

        return (service, stub);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(body);
            return await _responder(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string ProductJson(string handle, string name, long cents) =>
        "{\"product\":{\"id\":7126957,\"handle\":\"" + handle + "\",\"name\":\"" + name +
        "\",\"price_in_cents\":" + cents + ",\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false}}";

    private static string SubscriptionJson(int id, string state, string planHandle, string? reference = null) =>
        "{\"subscription\":{\"id\":" + id + ",\"state\":\"" + state + "\",\"reference\":\"" + (reference ?? string.Empty) +
        "\",\"customer_id\":4001,\"product_price_in_cents\":29900,\"currency\":\"USD\"," +
        "\"next_assessment_at\":\"2026-11-06T00:00:00.000Z\",\"current_period_ends_at\":\"2026-11-06T00:00:00.000Z\"," +
        "\"product\":{\"id\":1,\"handle\":\"" + planHandle + "\",\"name\":\"" + planHandle + " plan\",\"price_in_cents\":29900}}}";

    /// <summary>
    /// Full happy-path sandbox: family + plans resolve, customer is created on first sight,
    /// subscriptions are stored per customer across calls (what the provider really does).
    /// </summary>
    private static StubHandler Sandbox(bool customerMissingAtFirst = false)
    {
        var subscriptions = new List<string>();
        var customerCreated = !customerMissingAtFirst;

        return new StubHandler(async request =>
        {
            await Task.Yield();
            var path = request.RequestUri!.AbsolutePath;
            var method = request.Method;

            if (method == HttpMethod.Get && path.EndsWith("/product_families.json"))
                return Json(HttpStatusCode.OK, FamiliesJson);

            if (method == HttpMethod.Get && path.Contains("/products.json"))
                return Json(HttpStatusCode.OK, "[" + ProductJson("eshop-pro", "Eshop Pro", 29900) + "," + ProductJson("basic-plan", "Eshop Basic", 2900) + "]");

            if (method == HttpMethod.Get && path.Contains("/products/handle/eshop-pro"))
                return Json(HttpStatusCode.OK, ProductJson("eshop-pro", "Eshop Pro", 29900));

            if (method == HttpMethod.Get && path.Contains("/subscriptions.json"))
                return Json(HttpStatusCode.OK, "[" + string.Join(",", subscriptions) + "]");

            if (method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                var reference = ReadBodyField(request, "reference");
                var plan = ReadBodyField(request, "product_handle") ?? "unknown";
                var created = SubscriptionJson(500 + subscriptions.Count + 1, "active", plan, reference);
                subscriptions.Add(created);
                return Json(HttpStatusCode.Created, created);
            }

            if (method == HttpMethod.Post && path.EndsWith("/customers.json"))
            {
                customerCreated = true;
                return Json(HttpStatusCode.Created, CustomerJson);
            }

            if (method == HttpMethod.Get && (path == "/customers/lookup.json" && request.RequestUri.Query.Contains("reference=eshoponweb-")))
            {
                return customerCreated
                    ? Json(HttpStatusCode.OK, CustomerJson)
                    : Json(HttpStatusCode.NotFound, """{"errors":"Customer Not Found"}""");
            }

            return Json(HttpStatusCode.NotFound, $$"""{"errors":"unexpected stub route {{method}} {{path}}"}""");
        });
    }

    private static string? ReadBodyField(HttpRequestMessage request, string propertyName)
    {
        if (request.Content is null)
        {
            return null;
        }

        var json = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("subscription", out var sub) &&
               sub.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;
    }

    [Fact]
    public async Task ListPlansAsync_returns_products_from_configured_family()
    {
        var (service, stub) = BuildService(Sandbox());

        var plans = await service.ListPlansAsync(default);

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal("month", pro.IntervalUnit);

        // The family is resolved by handle at runtime; its numeric id goes into the products call.
        Assert.Contains(stub.Requests, r => r.RequestUri!.AbsolutePath.Contains("/product_families/3023074/"));
    }

    [Fact]
    public async Task SubscribeAsync_creates_customer_then_subscription_once()
    {
        var (service, stub) = BuildService(Sandbox(customerMissingAtFirst: true));

        var first = await service.SubscribeAsync(Email, "eshop-pro", default);
        var second = await service.SubscribeAsync(Email, "eshop-pro", default);

        Assert.False(first.AlreadySubscribed);
        Assert.True(second.AlreadySubscribed);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal("active", first.Subscription.State);
        Assert.NotNull(first.Subscription.NextBillingAt);
        Assert.Equal(29900, first.Subscription.PriceInCents);

        Assert.Equal(1, stub.Requests.Count(r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")));
        Assert.Equal(1, stub.Requests.Count(r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/customers.json")));

        var postIndex = stub.Requests.FindIndex(r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json"));
        var subscriptionBody = stub.RequestBodies[postIndex];
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscriptionBody);
        Assert.Contains("4001", subscriptionBody);
    }

    [Fact]
    public async Task SubscribeAsync_parallel_double_click_creates_exactly_one_subscription()
    {
        var (service, stub) = BuildService(Sandbox(customerMissingAtFirst: true));

        var tasks = Enumerable.Range(0, 3).Select(_ => service.SubscribeAsync(Email, "eshop-pro", default)).ToList();
        var outcomes = await Task.WhenAll(tasks);

        Assert.Equal(1, stub.Requests.Count(r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")));
        Assert.Equal(1, stub.Requests.Count(r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/customers.json")));
        Assert.Equal(1, outcomes.Count(o => !o.AlreadySubscribed));
        Assert.All(outcomes, o => Assert.Equal(outcomes[0].Subscription.Id, o.Subscription.Id));
    }

    [Fact]
    public async Task SubscribeAsync_converges_when_customer_create_loses_the_reference_race()
    {
        var subscriptions = new List<string>();
        var customerReadAttempts = 0;

        var stub = new StubHandler(async request =>
        {
            await Task.Yield();
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Get && path.Contains("/products/handle/eshop-pro"))
                return Json(HttpStatusCode.OK, ProductJson("eshop-pro", "Eshop Pro", 29900));

            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions.json"))
                return Json(HttpStatusCode.OK, "[" + string.Join(",", subscriptions) + "]");

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                var created = SubscriptionJson(777, "active", "eshop-pro", null);
                subscriptions.Add(created);
                return Json(HttpStatusCode.Created, created);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
            {
                // Another signup won the unique-reference race. The real provider body does
                // not match the generated error model — this also exercises the JsonException
                // trap on the error path.
                return Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Reference has already been taken"]}""");
            }

            if (request.Method == HttpMethod.Get && (path == "/customers/lookup.json" && request.RequestUri.Query.Contains("reference=eshoponweb-")))
            {
                customerReadAttempts++;
                // Missing on the first read, present afterwards (someone else created it).
                return customerReadAttempts == 1
                    ? Json(HttpStatusCode.NotFound, """{"errors":"Customer Not Found"}""")
                    : Json(HttpStatusCode.OK, CustomerJson);
            }

            return Json(HttpStatusCode.NotFound, $$"""{"errors":"unexpected stub route {{request.Method}} {{path}}"}""");
        });

        var (service, _) = BuildService(stub);

        var outcome = await service.SubscribeAsync(Email, "eshop-pro", default);

        Assert.False(outcome.AlreadySubscribed);
        Assert.Equal(777, outcome.Subscription.Id);
        Assert.True(customerReadAttempts >= 2);
    }

    [Fact]
    public async Task GetSubscriptionsForShopperAsync_returns_empty_when_no_customer_exists_yet()
    {
        var stub = new StubHandler(request =>
        {
            Assert.Equal("/customers/lookup.json", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(HttpStatusCode.NotFound, """{"errors":"Customer Not Found"}"""));
        });
        var (service, _) = BuildService(stub);

        var result = await service.GetSubscriptionsForShopperAsync(Email, default);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SubscribeAsync_maps_typed_provider_rejection_to_rejected_with_provider_errors()
    {
        var stub = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && (path == "/customers/lookup.json" && request.RequestUri.Query.Contains("reference=eshoponweb-")))
                return Task.FromResult(Json(HttpStatusCode.OK, CustomerJson));
            if (request.Method == HttpMethod.Get && path.Contains("/products/handle/"))
                return Task.FromResult(Json(HttpStatusCode.OK, ProductJson("eshop-pro", "Eshop Pro", 29900)));
            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions.json"))
                return Task.FromResult(Json(HttpStatusCode.OK, "[]"));
            return Task.FromResult(Json(HttpStatusCode.UnprocessableEntity,
                """{"errors":["Product handle is invalid"]}"""));
        });
        var (service, _) = BuildService(stub);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => service.SubscribeAsync(Email, "eshop-pro", default));

        Assert.Equal(MaxioBillingFailureKind.Rejected, ex.Kind);
        Assert.Contains("Product handle is invalid", ex.ProviderErrors);
    }

    [Fact]
    public async Task SubscribeAsync_unknown_transport_outcome_reconciles_and_never_resends_the_write()
    {
        var subscriptions = new List<string>();
        var stub = new StubHandler(async request =>
        {
            await Task.Yield();
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Get && (path == "/customers/lookup.json" && request.RequestUri.Query.Contains("reference=eshoponweb-")))
                return Json(HttpStatusCode.OK, CustomerJson);
            if (request.Method == HttpMethod.Get && path.Contains("/products/handle/"))
                return Json(HttpStatusCode.OK, ProductJson("eshop-pro", "Eshop Pro", 29900));
            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions.json"))
                return Json(HttpStatusCode.OK, "[" + string.Join(",", subscriptions) + "]");
            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                // The provider received and applied the write, but the response never came back.
                subscriptions.Add(SubscriptionJson(777, "active", "eshop-pro", null));
                throw new HttpRequestException("connection reset after the server received the request");
            }

            return Json(HttpStatusCode.NotFound, $$"""{"errors":"unexpected stub route {{request.Method}} {{path}}"}""");
        });
        var (service, stubClient) = BuildService(stub);

        var outcome = await service.SubscribeAsync(Email, "eshop-pro", default);

        Assert.True(outcome.AlreadySubscribed);
        Assert.Equal(777, outcome.Subscription.Id);

        // The stub only ever sees ONE POST /subscriptions.json: the SDK's transport retry is
        // blocked by the write guard before it reaches the network again.
        Assert.Equal(1, stubClient.Requests.Count(r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")));
    }

    [Fact]
    public async Task Unparseable_success_body_is_a_provider_problem_never_a_domain_absence()
    {
        var stub = new StubHandler(request => Task.FromResult(
            request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/customers/lookup.json"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>gateway error</html>", Encoding.UTF8, "text/html") }
                : Json(HttpStatusCode.NotFound, "{}")));
        var (service, _) = BuildService(stub);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => service.GetSubscriptionsForShopperAsync(Email, default));

        Assert.Equal(MaxioBillingFailureKind.UnparseableProviderResponse, ex.Kind);
    }

    [Fact]
    public async Task Missing_configured_family_is_a_configuration_failure()
    {
        var stub = new StubHandler(request =>
            Task.FromResult(Json(HttpStatusCode.OK, """[{"product_family":{"id":1,"handle":"other-family","name":"nope"}}]""")));
        var (service, _) = BuildService(stub);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() => service.ListPlansAsync(default));

        Assert.Equal(MaxioBillingFailureKind.Configuration, ex.Kind);
    }

    [Fact]
    public async Task Missing_configuration_section_throws_before_any_network_call()
    {
        var stub = Sandbox();
        var clientOptions = new MaxioAdvancedBillingClientOptions { Environment = ServerEnvironment.Us };
        clientOptions.Server.Production.Us.BaseUrl = BaseUrl;
        var client = new MaxioAdvancedBillingClient(new HttpClient(stub), clientOptions);
        var service = new MaxioSubscriptionBillingService(
            client,
            Options.Create(new MaxioOptions()),
            NullLogger<MaxioSubscriptionBillingService>.Instance);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() => service.ListPlansAsync(default));

        Assert.Equal(MaxioBillingFailureKind.Configuration, ex.Kind);
        Assert.Empty(stub.Requests);
    }
}


