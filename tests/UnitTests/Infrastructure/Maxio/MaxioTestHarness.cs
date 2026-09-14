using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// A recorded Maxio API call, so tests can assert on what actually went over the wire.
/// </summary>
public record RecordedRequest(HttpMethod Method, string PathAndQuery, string? Body);

/// <summary>
/// Stands in for the Maxio API. Responses are supplied by a callback keyed on the request,
/// which keeps each test's fixture next to the behaviour it is exercising.
/// </summary>
public sealed class StubMaxioHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, (HttpStatusCode Status, string Body)> _respond;

    public StubMaxioHandler(Func<RecordedRequest, (HttpStatusCode Status, string Body)> respond)
    {
        _respond = respond;
    }

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    /// <summary>Optional pause before responding, to widen the window for concurrency tests.</summary>
    public TimeSpan ResponseDelay { get; set; } = TimeSpan.Zero;

    public int CountOf(HttpMethod method, string pathContains) =>
        Requests.Count(r => r.Method == method && r.PathAndQuery.Contains(pathContains, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!.PathAndQuery, body);
        Requests.Enqueue(recorded);

        if (ResponseDelay > TimeSpan.Zero)
        {
            await Task.Delay(ResponseDelay, cancellationToken);
        }

        var (status, responseBody) = _respond(recorded);

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

public static class MaxioTestHarness
{
    public const string ProductFamilyHandle = "demo-plans";

    public static MaxioSettings Settings() => new()
    {
        ApiKey = "test-api-key",
        Subdomain = "test-site",
        ProductFamilyHandle = ProductFamilyHandle
    };

    public static MaxioSubscriptionService BuildService(StubMaxioHandler handler, MaxioSettings? settings = null)
    {
        settings ??= Settings();

        var httpClient = new HttpClient(handler);
        if (settings.IsConfigured)
        {
            httpClient.BaseAddress = settings.ResolveBaseAddress();
        }
        else
        {
            httpClient.BaseAddress = new Uri("https://unconfigured.invalid/");
        }

        var client = new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);

        return new MaxioSubscriptionService(
            client,
            Options.Create(settings),
            new MemoryCache(new MemoryCacheOptions()),
            new MaxioSubscriberLocks(),
            NullLogger<MaxioSubscriptionService>.Instance);
    }

    public static Subscriber Subscriber(string userKey = "DEMOUSER@MICROSOFT.COM") => new()
    {
        UserKey = userKey,
        Email = "demouser@microsoft.com",
        Organization = "eShopOnWeb"
    };

    public static string SiteJson(bool relationshipInvoicing = true, string currency = "USD") =>
        "{\"site\":{\"id\":1,\"name\":\"Test\",\"subdomain\":\"test-site\",\"currency\":\"" + currency +
        "\",\"relationship_invoicing_enabled\":" + (relationshipInvoicing ? "true" : "false") +
        ",\"default_payment_collection_method\":\"automatic\",\"test\":true}}";

    public static string ProductsJson() =>
        """
        [
          {"product":{"id":2,"name":"Basic Plan","handle":"basic-plan","description":null,"price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,"product_price_point_handle":"uuid:basic","product_family":{"id":9,"name":"Demo Plans","handle":"demo-plans"}}},
          {"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","description":"Everything in Basic, plus support","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,"product_price_point_handle":"uuid:pro","product_family":{"id":9,"name":"Demo Plans","handle":"demo-plans"}}},
          {"product":{"id":3,"name":"Retired Plan","handle":"retired-plan","price_in_cents":100,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":"2026-01-01T00:00:00+00:00","product_family":{"id":9,"name":"Demo Plans","handle":"demo-plans"}}},
          {"product":{"id":4,"name":"Card Required Plan","handle":"card-plan","price_in_cents":500,"interval":1,"interval_unit":"month","require_credit_card":true,"archived_at":null,"product_family":{"id":9,"name":"Demo Plans","handle":"demo-plans"}}}
        ]
        """;

    public static string CustomerJson(long id = 555, string reference = "eshoponweb:demouser@microsoft.com") =>
        "{\"customer\":{\"id\":" + id +
        ",\"first_name\":\"Demouser\",\"last_name\":\"User\",\"email\":\"demouser@microsoft.com\",\"reference\":\"" + reference +
        "\",\"created_at\":\"2026-09-06T12:00:00+00:00\"}}";

    public static string SubscriptionJson(
        long id = 900,
        string state = "active",
        string productHandle = "eshop-pro",
        long customerId = 555,
        string reference = "eshoponweb:demouser@microsoft.com:eshop-pro") =>
        "{\"subscription\":{\"id\":" + id +
        ",\"state\":\"" + state +
        "\",\"reference\":\"" + reference +
        "\",\"currency\":\"USD\",\"balance_in_cents\":29900,\"product_price_in_cents\":29900," +
        "\"payment_collection_method\":\"remittance\"," +
        "\"next_assessment_at\":\"2026-10-06T12:00:00+00:00\"," +
        "\"current_period_started_at\":\"2026-09-06T12:00:00+00:00\"," +
        "\"current_period_ends_at\":\"2026-10-06T12:00:00+00:00\"," +
        "\"activated_at\":\"2026-09-06T12:00:01+00:00\",\"canceled_at\":null," +
        "\"created_at\":\"2026-09-06T12:00:00+00:00\"," +
        "\"product\":{\"id\":1,\"name\":\"Pro Plan\",\"handle\":\"" + productHandle +
        "\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}," +
        "\"customer\":{\"id\":" + customerId +
        ",\"reference\":\"eshoponweb:demouser@microsoft.com\",\"email\":\"demouser@microsoft.com\"}}}";

    /// <summary>Wraps subscription bodies in the array shape the customer subscriptions list returns.</summary>
    public static string SubscriptionListJson(params string[] subscriptionEnvelopes) =>
        "[" + string.Join(",", subscriptionEnvelopes) + "]";

    public static string ErrorsJson(params string[] messages) =>
        "{\"errors\":[" + string.Join(",", messages.Select(m => "\"" + m.Replace("\"", "\\\"") + "\"")) + "]}";
}
