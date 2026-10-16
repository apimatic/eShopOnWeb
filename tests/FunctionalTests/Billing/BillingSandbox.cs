using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.FunctionalTests.Billing;

// In-process HTTP transport, exercising the actual plugin SDK serializers and exceptions.
internal sealed class BillingSandbox : HttpMessageHandler
{
    public readonly ConcurrentQueue<(HttpMethod Method, Uri Uri, string? Body)> Requests = new();
    private readonly Dictionary<string, JsonElement> _customers = new();
    private readonly Dictionary<string, JsonElement> _subscriptions = new();
    public string? Fault { get; set; }
    public Func<Uri, HttpResponseMessage>? Products { get; set; }
    public Func<Uri, HttpResponseMessage>? Reads { get; set; }
    public Func<JsonElement, JsonElement>? TransformSubscription { get; set; }
    public bool PublishSubscription { get; set; } = true;
    public bool PublishCustomer { get; set; } = true;
    public string? LastCustomerReference { get; private set; }
    public string? LastSubscriptionReference { get; private set; }
    public JsonElement? DeferredCustomer { get; private set; }
    public JsonElement? DeferredSubscription { get; private set; }
    public readonly MaxioSettings Settings = new() { ApiKey = "stub-key", Subdomain = "stub-site", ProductFamilyHandle = "family", BaseUrl = "http://billing.invalid/proxy" };
    private readonly DbContextOptions<CatalogContext> _dbOptions = new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    public CatalogContext Context() => new(_dbOptions);
    public MaxioSubscriptionBilling Service(CatalogContext db) => new(Client(), db, Options.Create(Settings), NullLogger<MaxioSubscriptionBilling>.Instance);
    public MaxioAdvancedBilling.MaxioAdvancedBillingClient Client() => BillingRegistration.CreateClient(new HttpClient(this, false), Settings, NullLoggerFactory.Instance);
    public static BillingShopper Shopper(string id = "one", string name = "ONE") => new(id, name, id + "@example.test");
    public static object Product(int id = 1, string handle = "pro", string family = "family", bool archived = false) => new
    {
        product = new { id, handle, name = "Plan " + handle, price_in_cents = 29900, interval = 1, interval_unit = "month", require_credit_card = false,
            archived_at = archived ? "2026-01-01T00:00:00Z" : null, product_family = new { handle = family } }
    };
    public static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    public static HttpResponseMessage Raw(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    public void PublishDeferred()
    {
        lock (_customers)
        {
            if (DeferredCustomer is { } c) _customers[c.GetProperty("reference").GetString()!] = c;
            if (DeferredSubscription is { } s) _subscriptions[s.GetProperty("reference").GetString()!] = s;
        }
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Enqueue((request.Method, uri, body));
        HttpResponseMessage response;
        lock (_customers)
        {
            var path = uri.AbsolutePath;
            if (path.EndsWith("/products.json")) response = Products?.Invoke(uri) ?? Json(new[] { Product(), Product(2, "basic"), Product(3, "foreign", "other"), Product(4, "old", archived: true) });
            else if (request.Method == HttpMethod.Get && Reads != null) response = Reads(uri);
            else if (path.EndsWith("/customers/lookup.json"))
            {
                var reference = QueryHelpers.ParseQuery(uri.Query)["reference"].ToString();
                response = _customers.TryGetValue(reference, out var c) ? Json(new { customer = c }) : Raw("{}", HttpStatusCode.NotFound);
            }
            else if (path.EndsWith("/subscriptions/lookup.json"))
            {
                var reference = QueryHelpers.ParseQuery(uri.Query)["reference"].ToString();
                response = _subscriptions.TryGetValue(reference, out var s) ? Json(new { subscription = s }) : Raw("{}", HttpStatusCode.NotFound);
            }
            else if (path.EndsWith("/customers.json") && request.Method == HttpMethod.Post)
            {
                if (Fault == "customer-422") return Raw("{\"errors\":42}", HttpStatusCode.UnprocessableEntity);
                using var document = JsonDocument.Parse(body!);
                var input = document.RootElement.GetProperty("customer");
                LastCustomerReference = input.GetProperty("reference").GetString()!;
                var c = JsonSerializer.SerializeToElement(new { id = _customers.Count + 10, reference = LastCustomerReference });
                DeferredCustomer = c;
                if (PublishCustomer) _customers[LastCustomerReference] = c;
                if (Fault == "customer-drop") throw new HttpRequestException("Simulated socket reset after commit");
                if (Fault == "customer-drift") return Raw("{\"customer\":\"drifted\"}");
                response = Json(new { customer = c });
            }
            else if (path.EndsWith("/subscriptions.json") && request.Method == HttpMethod.Post)
            {
                if (Fault == "subscription-422") return Raw("{\"errors\":[\"provider sensitive message\"]}", HttpStatusCode.UnprocessableEntity);
                using var document = JsonDocument.Parse(body!);
                var input = document.RootElement.GetProperty("subscription");
                var customerId = input.GetProperty("customer_id").GetInt32();
                var handle = input.GetProperty("product_handle").GetString();
                LastSubscriptionReference = input.GetProperty("reference").GetString()!;
                using var productDoc = JsonDocument.Parse(JsonSerializer.Serialize(Product(handle: handle!)));
                var s = JsonSerializer.SerializeToElement(new
                {
                    id = _subscriptions.Count + 100, reference = LastSubscriptionReference, customer = new { id = customerId },
                    product = productDoc.RootElement.GetProperty("product"), state = "active", product_price_in_cents = 28700,
                    currency = "USD", next_assessment_at = "2026-11-16T12:30:00Z", payment_collection_method = input.GetProperty("payment_collection_method").GetString()
                });
                s = TransformSubscription?.Invoke(s) ?? s;
                DeferredSubscription = s;
                if (PublishSubscription) _subscriptions[LastSubscriptionReference] = s;
                if (Fault == "subscription-drop") throw new HttpRequestException("Simulated socket reset after commit");
                if (Fault == "subscription-drift") return Raw("{\"subscription\":\"drifted\"}");
                response = Json(new { subscription = s });
            }
            else if (path.EndsWith("/subscriptions.json") && path.Contains("/customers/"))
            {
                var segments = path.Split('/');
                var id = int.Parse(segments[^2]);
                response = Json(_subscriptions.Values.Where(s => s.GetProperty("customer").GetProperty("id").GetInt32() == id).Select(s => new { subscription = s }).ToArray());
            }
            else throw new InvalidOperationException("Unexpected SDK request path: " + path);
        }
        response.RequestMessage = request;
        return response;
    }
}
