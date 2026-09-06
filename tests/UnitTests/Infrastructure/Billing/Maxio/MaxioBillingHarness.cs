using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// Fakes Maxio at the <see cref="HttpClient"/> seam — the one the SDK client is constructed from — so the
/// tests exercise the real registration, the real SDK serialization and the real handler pipeline, and
/// never reach the network.
/// </summary>
public sealed class StubMaxioHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    /// <summary>Every request that reached the network, in order. Retries append, so this is what you count.</summary>
    public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = new();

    public StubMaxioHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public int CountOf(HttpMethod method, string pathFragment) =>
        Requests.Count(request => request.Method == method && request.Path.Contains(pathFragment, StringComparison.Ordinal));

    public string BodyOf(HttpMethod method, string pathFragment) =>
        Requests.First(request => request.Method == method && request.Path.Contains(pathFragment, StringComparison.Ordinal)).Body;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.PathAndQuery, body));

        return _responder(request);
    }
}

internal static class MaxioBillingHarness
{
    public const string FamilyHandle = "eshop-subscribe";
    public const int FamilyId = 42;
    public const int CustomerId = 7;

    public static ISubscriptionBillingService Build(
        StubMaxioHandler handler,
        IDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-api-key",
            ["Maxio:Subdomain"] = "test-site",
            ["Maxio:ProductFamilyHandle"] = FamilyHandle,
            // Keep the tests fast: one attempt's worth of budget, and the SDK's retry floor of 1.
            ["Maxio:MaxRetries"] = "1",
            ["Maxio:AttemptTimeout"] = "00:00:05",
            ["Maxio:RequestTimeout"] = "00:00:15"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaxioSubscriptionBilling(configuration);

        // Replace only the primary handler, so the integration's own tracking handler stays in the pipeline
        // and write-once behaviour is exercised rather than bypassed.
        services.AddHttpClient(MaxioBillingServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        return services.BuildServiceProvider().GetRequiredService<ISubscriptionBillingService>();
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    public static HttpResponseMessage NotFound() => Json(HttpStatusCode.NotFound, Serialize(new { errors = new[] { "Customer not found" } }));

    // Wire names are snake_case; the anonymous members below are spelled exactly as they appear on the wire.
    private static string Serialize(object value) => JsonSerializer.Serialize(value);

    public static string ProductFamilies(string handle = FamilyHandle, int id = FamilyId) =>
        Serialize(new[] { new { product_family = new { id, handle, name = "eShop Subscribe" } } });

    public static string Site(string currency = "USD", bool relationshipInvoicing = true) =>
        Serialize(new
        {
            site = new
            {
                currency,
                relationship_invoicing_enabled = relationshipInvoicing,
                default_payment_collection_method = "automatic",
                subdomain = "test-site"
            }
        });

    public static string Product(
        string handle,
        string name,
        long priceInCents,
        bool requireCreditCard = false,
        bool requestCreditCard = true,
        DateTimeOffset? archivedAt = null) =>
        Serialize(new
        {
            product = new
            {
                id = 5,
                handle,
                name,
                description = name + " description",
                price_in_cents = priceInCents,
                interval = 1,
                interval_unit = "month",
                require_credit_card = requireCreditCard,
                request_credit_card = requestCreditCard,
                archived_at = archivedAt
            }
        });

    public static string Products(params string[] products) => "[" + string.Join(",", products) + "]";

    public static string Customer(int id = CustomerId, string reference = "eshoponweb:demo@example.com") =>
        Serialize(new
        {
            customer = new
            {
                id,
                reference,
                email = "demo@example.com",
                first_name = "Demo",
                last_name = "eShopOnWeb"
            }
        });

    public static readonly DateTimeOffset PeriodStartedAt = new(2026, 9, 6, 9, 44, 2, TimeSpan.Zero);
    public static readonly DateTimeOffset PeriodEndsAt = new(2026, 10, 6, 9, 44, 2, TimeSpan.Zero);

    public static string Subscription(
        int id,
        string state,
        string planHandle,
        string planName,
        long priceInCents) =>
        Serialize(new
        {
            subscription = new
            {
                id,
                state,
                currency = "USD",
                product_price_in_cents = priceInCents,
                current_period_started_at = PeriodStartedAt,
                current_period_ends_at = PeriodEndsAt,
                created_at = PeriodStartedAt,
                customer = new { id = CustomerId },
                product = new
                {
                    id = 5,
                    handle = planHandle,
                    name = planName,
                    price_in_cents = priceInCents,
                    interval = 1,
                    interval_unit = "month"
                }
            }
        });

    public static string Subscriptions(params string[] subscriptions) => "[" + string.Join(",", subscriptions) + "]";
}
