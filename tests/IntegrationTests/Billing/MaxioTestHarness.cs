using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>One request as the billing provider would have received it.</summary>
public sealed class RecordedRequest
{
    public RecordedRequest(HttpMethod method, Uri uri, string body)
    {
        Method = method;
        Uri = uri;
        Body = body;
    }

    public HttpMethod Method { get; }
    public Uri Uri { get; }
    public string Body { get; }

    public bool Matches(HttpMethod method, string pathFragment) =>
        Method == method && Uri.AbsoluteUri.Contains(pathFragment, StringComparison.Ordinal);
}

/// <summary>
/// The seam for these tests: the SDK client takes an HttpClient, so a fake transport lets the whole
/// billing boundary run - request building, deserialization, retries and the error ladder - with no
/// network call and no dependency on SDK internals.
/// </summary>
public sealed class StubTransport : HttpMessageHandler
{
    private readonly Func<RecordedRequest, HttpResponseMessage> _responder;
    private readonly List<RecordedRequest> _requests = new List<RecordedRequest>();
    private readonly object _sync = new object();

    public StubTransport(Func<RecordedRequest, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    /// <summary>Every request the provider would have seen, retries included - this is what to count.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_sync)
            {
                return _requests.ToList();
            }
        }
    }

    public int CountOf(HttpMethod method, string pathFragment) =>
        Requests.Count(request => request.Matches(method, pathFragment));

    public RecordedRequest FirstOf(HttpMethod method, string pathFragment) =>
        Requests.First(request => request.Matches(method, pathFragment));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync();

        var recorded = new RecordedRequest(request.Method, request.RequestUri!, body);

        lock (_sync)
        {
            _requests.Add(recorded);
        }

        return _responder(recorded);
    }

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    public static HttpResponseMessage Ok(string json) => Json(HttpStatusCode.OK, json);
}

public static class MaxioTestHarness
{
    public const string FamilyHandle = "test-family";

    public static MaxioSettings Settings() => new MaxioSettings
    {
        ApiKey = "test-api-key",
        Subdomain = "test-site",
        ProductFamilyHandle = FamilyHandle
    };

    public static MaxioSubscriptionBillingService CreateService(StubTransport transport, MaxioSettings settings = null!)
    {
        settings ??= Settings();

        // The write-once handler sits in the pipeline exactly as it does in production, so the
        // "one write reaches the provider" guarantee is what these tests actually exercise.
        var httpClient = new HttpClient(new MaxioWriteOnceHandler(NullLogger<MaxioWriteOnceHandler>.Instance)
        {
            InnerHandler = transport
        });

        var options = new MaxioAdvancedBillingClientOptions
        {
            // Retries stay on - they are part of what is under test - but without the real backoff.
            Retry = RetryOptions.Default() with
            {
                Delay = TimeSpan.FromMilliseconds(1),
                MaxJitter = TimeSpan.Zero
            }
        };

        options.Server.Production.Us.Site = settings.Subdomain ?? "test-site";

        var client = new MaxioAdvancedBillingClient(httpClient, options);

        return new MaxioSubscriptionBillingService(
            client,
            Options.Create(settings),
            new SubscriberLockRegistry(),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    // ------------------------------------------------------------- fixtures

    public static string ProductsJson(params string[] products) => "[" + string.Join(",", products) + "]";

    public static string Product(string handle, string name, long priceInCents, string archivedAt = null!) =>
        $@"{{""product"":{{
            ""id"": 1,
            ""handle"": ""{handle}"",
            ""name"": ""{name}"",
            ""description"": ""{name} description"",
            ""price_in_cents"": {priceInCents},
            ""interval"": 1,
            ""interval_unit"": ""month"",
            ""archived_at"": {(archivedAt is null ? "null" : "\"" + archivedAt + "\"")},
            ""product_family"": {{ ""id"": 10, ""handle"": ""{FamilyHandle}"", ""name"": ""Test Family"" }}
        }}}}";

    public static string CustomerJson(int id, string reference) =>
        $@"{{""customer"":{{ ""id"": {id}, ""reference"": ""{reference}"", ""email"": ""shopper@example.com"",
            ""first_name"": ""shopper"", ""last_name"": ""eShopOnWeb"" }}}}";

    public static string SubscriptionsJson(params string[] subscriptions) => "[" + string.Join(",", subscriptions) + "]";

    public static string SubscriptionJson(
        int id,
        string state,
        string planHandle,
        long priceInCents,
        int customerId = 500,
        string customerReference = "eshoponweb-shopper@example.com") =>
        $@"{{""subscription"":{{
            ""id"": {id},
            ""state"": ""{state}"",
            ""next_assessment_at"": ""2026-10-06T16:47:12+05:00"",
            ""current_period_ends_at"": ""2026-10-06T16:47:12+05:00"",
            ""created_at"": ""2026-09-06T16:47:12+05:00"",
            ""currency"": ""USD"",
            ""product_price_in_cents"": {priceInCents},
            ""product"": {{ ""id"": 1, ""handle"": ""{planHandle}"", ""name"": ""Plan {planHandle}"",
                ""price_in_cents"": {priceInCents}, ""interval"": 1, ""interval_unit"": ""month"" }},
            ""customer"": {{ ""id"": {customerId}, ""reference"": ""{customerReference}"" }}
        }}}}";
}
