using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApiUnitTests.Billing;

/// <summary>
/// HttpClient seam for the Maxio SDK: answers by "METHOD /path" and records every request (and its body,
/// captured while still readable). No network traffic.
/// </summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _routes = new();

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    public StubHandler On(HttpMethod method, string path, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _routes[$"{method.Method} {path}"] = responder;
        return this;
    }

    public StubHandler On(HttpMethod method, string path, Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        On(method, path, (r, _) => Task.FromResult(responder(r)));

    public StubHandler OnJson(HttpMethod method, string path, HttpStatusCode status, string json) =>
        On(method, path, _ => Json(status, json));

    public int Count(HttpMethod method, string path) =>
        Requests.Count(r => r.Method == method && Uri.UnescapeDataString(r.RequestUri!.AbsolutePath) == path);

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        var key = $"{request.Method.Method} {Uri.UnescapeDataString(request.RequestUri!.AbsolutePath)}";
        if (!_routes.TryGetValue(key, out var responder))
        {
            return Json(HttpStatusCode.NotImplemented, "{}");
        }
        var response = await responder(request, cancellationToken);
        response.RequestMessage = request;
        return response;
    }
}

public static class MaxioJson
{
    public static string Product(int id, string handle, string name, long priceInCents, bool archived = false) =>
        $$$"""{"product":{"id":{{{id}}},"handle":"{{{handle}}}","name":"{{{name}}}","description":"{{{name}}} description","price_in_cents":{{{priceInCents}}},"interval":1,"interval_unit":"month","archived_at":{{{(archived ? "\"2024-01-01T00:00:00Z\"" : "null")}}}}}""";

    public static string Products(params string[] products) => "[" + string.Join(",", products) + "]";

    public static string StandardPlans() => Products(
        Product(1, "eshop-pro", "Pro Plan", 29900),
        Product(2, "basic-plan", "Basic Plan", 2900),
        Product(3, "old-plan", "Old Plan", 100, archived: true));

    public static string Customer(int id, string reference) =>
        $$$"""{"customer":{"id":{{{id}}},"reference":"{{{reference}}}","email":"shopper@example.com","first_name":"Shopper","last_name":"Customer"}}""";

    public static string Subscription(int id, string? reference, string productHandle = "eshop-pro", string state = "active", string createdAt = "2026-10-20T10:00:00Z") =>
        $$$"""{"subscription":{"id":{{{id}}},"state":"{{{state}}}","reference":{{{(reference == null ? "null" : "\"" + reference + "\"")}}},"product_price_in_cents":29900,"currency":"USD","next_assessment_at":"2026-11-20T10:00:00Z","current_period_ends_at":"2026-11-20T10:00:00Z","created_at":"{{{createdAt}}}","activated_at":"{{{createdAt}}}","payment_collection_method":"remittance","product":{"id":1,"handle":"{{{productHandle}}}","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}""" + "}";

    public static string List(params string[] items) => "[" + string.Join(",", items) + "]";
}

public static class MaxioTestFactory
{
    public const string BaseUrl = "https://stub.maxio.test";

    public static MaxioSettings Settings() => new()
    {
        ApiKey = "test-placeholder-key",
        Subdomain = "placeholder-site",
        ProductFamilyHandle = "eshop-subscribe",
        BaseUrl = BaseUrl,
    };

    public static MaxioBillingGateway Gateway(StubHandler stub, MaxioGatewayLimits? limits = null, MaxioSettings? settings = null)
    {
        settings ??= Settings();
        var options = MaxioServiceCollectionExtensions.CreateClientOptions(settings, NullLoggerFactory.Instance);
        var client = new MaxioAdvancedBillingClient(new HttpClient(stub), options);
        return new MaxioBillingGateway(client, Options.Create(settings), limits ?? new MaxioGatewayLimits(), NullLogger<MaxioBillingGateway>.Instance);
    }
}
