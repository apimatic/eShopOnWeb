using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Stands in for Maxio at the HttpClient seam. Routes by "METHOD /path"; records every request
/// (with its body, buffered while still readable) so tests can count what reached "Maxio".
/// </summary>
public sealed class MaxioStub : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> _routes = new();

    public ConcurrentQueue<(string Route, string? Query, string? Body)> Requests { get; } = new();

    public void On(string route, Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) => _routes[route] = responder;

    public void On(string route, HttpStatusCode status, string json) =>
        On(route, _ => Task.FromResult(Json(status, json)));

    public int Count(string route) => Requests.Count(r => r.Route == route);

    public IEnumerable<string?> Bodies(string route) => Requests.Where(r => r.Route == route).Select(r => r.Body);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var route = $"{request.Method} {Uri.UnescapeDataString(request.RequestUri!.AbsolutePath)}";
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue((route, request.RequestUri.Query, body));

        if (!_routes.TryGetValue(route, out var responder))
        {
            return Json(HttpStatusCode.NotFound, "{\"errors\":[\"no stub for " + route + "\"]}");
        }
        var response = await responder(request);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // --- canned Maxio payloads -------------------------------------------------------------

    public const string ProductsRoute = "GET /product_families/handle:test-family/products.json";
    public const string CustomerLookupRoute = "GET /customers/lookup.json";
    public const string CreateCustomerRoute = "POST /customers.json";
    public const string CustomerSubscriptionsRoute = "GET /customers/501/subscriptions.json";
    public const string CreateSubscriptionRoute = "POST /subscriptions.json";
    public const string SubscriptionLookupRoute = "GET /subscriptions/lookup.json";

    public const int CustomerId = 501;
    public const int SubscriptionId = 9001;

    public static string Product(int id, string handle, string name, long priceInCents, string? archivedAt = null)
    {
        var archived = archivedAt is null ? "" : $",\"archived_at\":\"{archivedAt}\"";
        return $$$"""{"product":{"id":{{{id}}},"handle":"{{{handle}}}","name":"{{{name}}}","price_in_cents":{{{priceInCents}}},"interval":1,"interval_unit":"month"{{{archived}}}}}""";
    }

    public static string Customer(int id = CustomerId) =>
        $$$"""{"customer":{"id":{{{id}}},"reference":"eshop:demouser@microsoft.com","first_name":"demouser","last_name":"eShopOnWeb","email":"demouser@microsoft.com"}}""";

    public static string Subscription(string planHandle = "test-pro", string state = "active", int id = SubscriptionId) =>
        $$$"""{"subscription":{"id":{{{id}}},"state":"{{{state}}}","product_price_in_cents":29900,"currency":"USD","current_period_ends_at":"2030-01-01T00:00:00Z","next_assessment_at":"2030-01-01T00:00:00Z","reference":"ref","customer":{"id":{{{CustomerId}}} },"product":{"id":1,"handle":"{{{planHandle}}}","name":"Test Pro"} } }""";

    /// <summary>A stub for a new user subscribing successfully, with an empty Maxio account.</summary>
    public static MaxioStub NewUserHappyPath()
    {
        var stub = new MaxioStub();
        stub.On(ProductsRoute, HttpStatusCode.OK,
            $"[{Product(1, "test-pro", "Test Pro", 29900)},{Product(2, "test-basic", "Test Basic", 2900)},{Product(3, "old-plan", "Old", 100, "2020-01-01T00:00:00Z")}]");
        stub.On(CustomerLookupRoute, HttpStatusCode.NotFound, "");
        stub.On(CreateCustomerRoute, HttpStatusCode.Created, Customer());
        stub.On(CustomerSubscriptionsRoute, HttpStatusCode.OK, "[]");
        stub.On(CreateSubscriptionRoute, HttpStatusCode.Created, Subscription());
        stub.On(SubscriptionLookupRoute, HttpStatusCode.NotFound, "");
        return stub;
    }
}
