using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace PublicApiIntegrationTests.Maxio;

/// <summary>
/// In-process fake of the Maxio Advanced Billing wire surface used by the
/// subscription integration. Routes loosely on documented resource paths, keeps
/// in-memory customer/subscription state, records every request, and exposes
/// counters so tests can assert exactly-once side effects.
/// </summary>
public sealed class StubMaxioHandler : HttpMessageHandler
{
    public sealed record RecordedRequest(string Method, string Path, string Query, string? Body);

    public const string FamilyId = "501";
    public const string FamilyHandle = "test-family";
    public const string ProHandle = "test-pro";
    public const int ProPriceInCents = 29900;
    public const string BasicHandle = "test-basic";
    public const int BasicPriceInCents = 2900;

    public List<RecordedRequest> Requests { get; } = new();
    public int CustomerCreates;
    public int SubscriptionCreates;

    private sealed record Customer(int Id, string Email, string Reference);

    private sealed record Subscription(int Id, string Reference, int CustomerId, string ProductHandle);

    private readonly List<Customer> _customers = new();
    private readonly List<Subscription> _subscriptions = new();
    private int _customerSeq = 7000;
    private int _subscriptionSeq = 9000;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        var uri = request.RequestUri!;
        var bodyText = body;
        Requests.Add(new RecordedRequest(request.Method.Method, uri.AbsolutePath, uri.Query, bodyText));
        Console.WriteLine($"STUB REQUEST: {request.Method.Method} {uri.AbsolutePath}{uri.Query}");

        var response = Route(request.Method.Method, uri.AbsolutePath, uri.Query, body);
        return Task.FromResult(response);
    }

    private HttpResponseMessage Route(string method, string path, string query, string? body)
    {
        if (method == "GET" && path == "/product_families.json")
        {
            return Json(HttpStatusCode.OK,
                """[{"product_family":{"id":501,"name":"Test Family","handle":"test-family"}}]""");
        }

        if (method == "GET" && path.StartsWith("/product_families/") && path.EndsWith("/products.json"))
        {
            return Json(HttpStatusCode.OK, ProductsJson());
        }

        if (method == "GET" && path == "/customers/lookup.json")
        {
            var reference = QueryValue(query, "reference");
            var customer = _customers.FirstOrDefault(c => c.Reference == reference);
            return customer is null
                ? Json(HttpStatusCode.NotFound, """{"errors":["not found"]}""")
                : Json(HttpStatusCode.OK, CustomerJson(customer));
        }

        if (method == "GET" && path == "/customers.json")
        {
            var q = QueryValue(query, "q");
            var matches = _customers
                .Where(c => string.IsNullOrEmpty(q) || c.Email.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Select(CustomerJson);
            return Json(HttpStatusCode.OK, "[" + string.Join(",", matches) + "]");
        }

        if (method == "POST" && path == "/customers.json")
        {
            CustomerCreates++;
            using var doc = JsonDocument.Parse(body!);
            var c = doc.RootElement.GetProperty("customer");
            var email = c.GetProperty("email").GetString()!;
            var reference = c.TryGetProperty("reference", out var r) ? r.GetString() : null;
            if (reference is not null && _customers.Any(x => x.Reference == reference))
            {
                return Json(HttpStatusCode.UnprocessableEntity,
                    """{"errors":{"reference":["has already been taken"]}}""");
            }
            var customer = new Customer(++_customerSeq, email, reference ?? string.Empty);
            _customers.Add(customer);
            return Json(HttpStatusCode.Created, CustomerJson(customer));
        }

        if (method == "GET" && path == "/subscriptions/lookup.json")
        {
            var reference = QueryValue(query, "reference");
            var subscription = _subscriptions.FirstOrDefault(s => s.Reference == reference);
            return subscription is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(string.Empty) }
                : Json(HttpStatusCode.OK, SubscriptionJson(subscription));
        }

        if (method == "POST" && path == "/subscriptions.json")
        {
            using var doc = JsonDocument.Parse(body!);
            var s = doc.RootElement.GetProperty("subscription");
            var productHandle = s.GetProperty("product_handle").GetString()!;
            if (productHandle != ProHandle && productHandle != BasicHandle)
            {
                return Json(HttpStatusCode.UnprocessableEntity,
                    """{"errors":["Product handle 'no-such-plan' does not exist."]}""");
            }
            var reference = s.TryGetProperty("reference", out var r) ? r.GetString() : null;
            var existing = _subscriptions.FirstOrDefault(x => reference is not null && x.Reference == reference);
            if (existing is not null)
            {
                return Json(HttpStatusCode.Created, SubscriptionJson(existing));
            }
            var customerId = s.GetProperty("customer_id").GetInt32();
            var subscription = new Subscription(++_subscriptionSeq, reference ?? string.Empty, customerId, productHandle);
            _subscriptions.Add(subscription);
            SubscriptionCreates++;
            return Json(HttpStatusCode.Created, SubscriptionJson(subscription));
        }

        if (method == "GET" && path.StartsWith("/customers/") && path.EndsWith("/subscriptions.json"))
        {
            var segment = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[1];
            var customerId = int.Parse(segment);
            var subs = _subscriptions
                .Where(s => s.CustomerId == customerId)
                .Select(SubscriptionJson);
            return Json(HttpStatusCode.OK, "[" + string.Join(",", subs) + "]");
        }

        Console.WriteLine($"STUB MISS: {method} {path}{query}");
        return Json(HttpStatusCode.NotFound, """{"errors":["no such route"]}""");
    }

    private static string ProductsJson() =>
        """
        [{"product":{"id":601,"name":"Test Pro","handle":"test-pro","description":"Pro plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"taxable":false,"product_family":{"id":501,"name":"Test Family","handle":"test-family"}}},
         {"product":{"id":602,"name":"Test Basic","handle":"test-basic","description":"Basic plan","price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,"taxable":false,"product_family":{"id":501,"name":"Test Family","handle":"test-family"}}}]
        """;

    private static string CustomerJson(Customer customer)
    {
        var (firstName, lastName) = SplitName(customer.Email);
        return $@"{{""customer"":{{""id"":{customer.Id},""first_name"":""{firstName}"",""last_name"":""{lastName}"",""email"":""{customer.Email}"",""reference"":""{customer.Reference}""}}}}";
    }

    private string SubscriptionJson(Subscription subscription)
    {
        var (priceInCents, productName) = subscription.ProductHandle == ProHandle
            ? (ProPriceInCents, "Test Pro")
            : (BasicPriceInCents, "Test Basic");
        var customer = _customers.First(c => c.Id == subscription.CustomerId);
        return $@"{{""subscription"":{{""id"":{subscription.Id},""state"":""active"",""reference"":""{subscription.Reference}"",""product_price_in_cents"":{priceInCents},""currency"":""USD"",""current_period_ends_at"":""2026-10-09T00:00:00Z"",""current_period_started_at"":""2026-09-09T00:00:00Z"",""activated_at"":""2026-09-09T00:00:01Z"",""customer"":{{""id"":{customer.Id},""email"":""{customer.Email}""}},""product"":{{""id"":{(subscription.ProductHandle == ProHandle ? 601 : 602)},""name"":""{productName}"",""handle"":""{subscription.ProductHandle}"",""price_in_cents"":{priceInCents},""interval"":1,""interval_unit"":""month""}}}}}}";
    }

    private static (string FirstName, string LastName) SplitName(string email)
    {
        var local = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var parts = local.Split('.', '_', '-');
        return (parts[0], parts.Length > 1 ? parts[1] : "Shopper");
    }

    private static string QueryValue(string query, string key)
    {
        var parsed = HttpUtility.ParseQueryString(query);
        return parsed[key] ?? string.Empty;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) =>
        new(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
}
