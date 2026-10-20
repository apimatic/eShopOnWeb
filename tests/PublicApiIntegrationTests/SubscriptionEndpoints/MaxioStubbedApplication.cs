using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// PublicApi host whose Maxio traffic is answered by an in-process fake instead of the network.
/// </summary>
public sealed class MaxioStubbedApplication : WebApplicationFactory<Program>
{
    public FakeMaxio Maxio { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
            services.AddHttpClient(MaxioBillingServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Maxio));
    }
}

/// <summary>
/// A minimal stateful Maxio: one product family with two plans, customers and subscriptions keyed by reference.
/// </summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, int> _customers = new();
    private readonly ConcurrentDictionary<string, (int Id, int CustomerId, string Handle)> _subscriptions = new();
    private int _nextId = 1000;

    public int SubscriptionCreates;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);

        if (request.Method == HttpMethod.Get && path == "/product_families/handle:test-family/products.json")
        {
            return Json(HttpStatusCode.OK,
                """[{"product":{"id":1,"handle":"eshop-pro","name":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}},""" +
                """{"product":{"id":2,"handle":"basic-plan","name":"Basic","price_in_cents":2900,"interval":1,"interval_unit":"month"}}]""");
        }

        if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
        {
            var reference = query["reference"]!;
            return _customers.TryGetValue(reference, out var id)
                ? Json(HttpStatusCode.OK, CustomerJson(id, reference))
                : Json(HttpStatusCode.NotFound, """{"errors":["Not Found"]}""");
        }

        if (request.Method == HttpMethod.Post && path == "/customers.json")
        {
            var reference = Field(body, "reference");
            var id = _customers.GetOrAdd(reference, _ => Interlocked.Increment(ref _nextId));
            return Json(HttpStatusCode.Created, CustomerJson(id, reference));
        }

        if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
        {
            var reference = query["reference"]!;
            return _subscriptions.TryGetValue(reference, out var s)
                ? Json(HttpStatusCode.OK, SubscriptionJson(s.Id, reference, s.Handle))
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
        {
            Interlocked.Increment(ref SubscriptionCreates);
            var reference = Field(body, "reference");
            var handle = Field(body, "product_handle");
            var customerId = int.Parse(Number(body, "customer_id"));
            var s = _subscriptions.GetOrAdd(reference, _ => (Interlocked.Increment(ref _nextId), customerId, handle));
            return Json(HttpStatusCode.Created, SubscriptionJson(s.Id, reference, s.Handle));
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/customers/") && path.EndsWith("/subscriptions.json"))
        {
            var customerId = int.Parse(path.Split('/')[2]);
            var items = _subscriptions.Where(kv => kv.Value.CustomerId == customerId)
                .Select(kv => SubscriptionJson(kv.Value.Id, kv.Key, kv.Value.Handle));
            return Json(HttpStatusCode.OK, "[" + string.Join(",", items) + "]");
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/subscriptions/") && path.EndsWith(".json"))
        {
            var id = int.Parse(path["/subscriptions/".Length..^".json".Length]);
            var match = _subscriptions.FirstOrDefault(kv => kv.Value.Id == id);
            return match.Key is null
                ? Json(HttpStatusCode.NotFound, "{}")
                : Json(HttpStatusCode.OK, SubscriptionJson(id, match.Key, match.Value.Handle));
        }

        return Json(HttpStatusCode.NotFound, """{"errors":["no fake route"]}""");
    }

    private static string Field(string json, string name) =>
        System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject().First().Value.GetProperty(name).GetString()!;

    private static string Number(string json, string name) =>
        System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject().First().Value.GetProperty(name).GetRawText();

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string CustomerJson(int id, string reference) =>
        $$"""{"customer":{"id":{{id}},"reference":"{{reference}}","email":"{{reference}}"} }""";

    private static string SubscriptionJson(int id, string reference, string handle) =>
        "{\"subscription\":{\"id\":" + id + ",\"state\":\"active\",\"reference\":\"" + reference + "\",\"currency\":\"USD\"," +
        "\"product_price_in_cents\":" + (handle == "eshop-pro" ? 29900 : 2900) + "," +
        "\"current_period_ends_at\":\"2026-11-20T10:00:00-05:00\",\"created_at\":\"2026-10-20T10:00:00-05:00\"," +
        "\"product\":{\"id\":1,\"handle\":\"" + handle + "\",\"name\":\"Plan\",\"interval\":1,\"interval_unit\":\"month\"}}}";
}
