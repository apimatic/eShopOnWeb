using System;
using System.Collections.Generic;
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

/// <summary>PublicApi host whose Maxio HttpClient talks to an in-process fake instead of a Maxio site.</summary>
public class MaxioStubbedApplication : WebApplicationFactory<Program>
{
    public FakeMaxio Maxio { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
            services.AddHttpClient(MaxioBillingServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Maxio));
    }
}

/// <summary>A minimal stateful Maxio site: one product family, customers and subscriptions created on demand.</summary>
public class FakeMaxio : HttpMessageHandler
{
    private readonly object _lock = new();
    private readonly Dictionary<string, int> _customersByReference = new();
    private readonly Dictionary<string, (int Id, string Handle)> _subscriptionsByReference = new();
    private readonly List<string> _routes = new();

    public bool Unreachable { get; set; }

    public int Count(string route)
    {
        lock (_lock)
        {
            return _routes.Count(r => r == route);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Unreachable)
        {
            throw new HttpRequestException("connection refused");
        }

        var path = request.RequestUri!.AbsolutePath;
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var route = $"{request.Method} {path}";

        lock (_lock)
        {
            _routes.Add(route);
            switch (route)
            {
                case "GET /product_families/handle%3Atest-family/products.json":
                    return Json(HttpStatusCode.OK, """
                        [
                          { "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "require_credit_card": false } },
                          { "product": { "id": 2, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "require_credit_card": false } }
                        ]
                        """);
                case "GET /site.json":
                    return Json(HttpStatusCode.OK, """{ "site": { "id": 1, "relationship_invoicing_enabled": true } }""");
                case "GET /customers/lookup.json":
                    return _customersByReference.TryGetValue(query["reference"]!, out var customerId)
                        ? Json(HttpStatusCode.OK, $$"""{ "customer": { "id": {{customerId}} } }""")
                        : Json(HttpStatusCode.NotFound, "{}");
                case "POST /customers.json":
                {
                    var reference = Extract(body, "reference");
                    var id = 100 + _customersByReference.Count;
                    _customersByReference[reference] = id;
                    return Json(HttpStatusCode.Created, $$"""{ "customer": { "id": {{id}}, "reference": "{{reference}}" } }""");
                }
                case "GET /subscriptions/lookup.json":
                    return _subscriptionsByReference.TryGetValue(query["reference"]!, out var found)
                        ? Json(HttpStatusCode.OK, Subscription(found.Id, found.Handle, query["reference"]!))
                        : new HttpResponseMessage(HttpStatusCode.NotFound);
                case "POST /subscriptions.json":
                {
                    var reference = Extract(body, "reference");
                    var handle = Extract(body, "product_handle");
                    var id = 500 + _subscriptionsByReference.Count;
                    _subscriptionsByReference[reference] = (id, handle);
                    return Json(HttpStatusCode.Created, Subscription(id, handle, reference));
                }
            }

            if (path.StartsWith("/subscriptions/", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                var id = int.Parse(path["/subscriptions/".Length..^".json".Length]);
                var match = _subscriptionsByReference.Single(s => s.Value.Id == id);
                return Json(HttpStatusCode.OK, Subscription(id, match.Value.Handle, match.Key));
            }

            if (path.StartsWith("/customers/", StringComparison.Ordinal) && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                var items = _subscriptionsByReference.Select(s => Subscription(s.Value.Id, s.Value.Handle, s.Key));
                return Json(HttpStatusCode.OK, "[" + string.Join(",", items) + "]");
            }
        }

        return Json(HttpStatusCode.NotFound, "{}");
    }

    private static string Subscription(int id, string handle, string reference) => $$"""
        {
          "subscription": {
            "id": {{id}},
            "state": "active",
            "product_price_in_cents": {{(handle == "eshop-pro" ? 29900 : 2900)}},
            "currency": "USD",
            "reference": "{{reference}}",
            "current_period_ends_at": "2026-11-20T10:00:00Z",
            "next_assessment_at": "2026-11-20T10:00:00Z",
            "created_at": "2026-10-20T10:00:00Z",
            "product": { "id": 1, "handle": "{{handle}}", "name": "{{handle}}" }
          }
        }
        """;

    private static string Extract(string json, string property)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        var inner = root.EnumerateObject().First().Value;
        return inner.GetProperty(property).ToString();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
