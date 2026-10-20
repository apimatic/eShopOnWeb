using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>
/// Fake Maxio transport: answers SDK requests by method + path and records what it received.
/// </summary>
public sealed class MaxioStub : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> _routes = new();

    public ConcurrentQueue<(HttpMethod Method, string PathAndQuery, string? Body)> Requests { get; } = new();

    public MaxioStub OnAsync(HttpMethod method, string path, Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        _routes[$"{method} {path}"] = responder;
        return this;
    }

    public MaxioStub On(HttpMethod method, string path, Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        OnAsync(method, path, request => Task.FromResult(responder(request)));

    public MaxioStub On(HttpMethod method, string path, HttpStatusCode status, string json) =>
        On(method, path, _ => Json(status, json));

    public int Count(HttpMethod method, string path) =>
        Requests.Count(r => r.Method == method && Uri.UnescapeDataString(r.PathAndQuery.Split('?')[0]) == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the body now: the SDK disposes request content after each attempt.
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        Requests.Enqueue((request.Method, request.RequestUri.PathAndQuery, body));

        if (!_routes.TryGetValue($"{request.Method} {path}", out var responder))
        {
            return Json(HttpStatusCode.NotFound, "{\"errors\":[\"no stub route\"]}");
        }

        var response = await responder(request);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    public static string ProductsJson(params (int Id, string Handle, string Name, long PriceInCents)[] products) =>
        "[" + string.Join(",", products.Select(p =>
            $$$"""{"product":{"id":{{{p.Id}}},"handle":"{{{p.Handle}}}","name":"{{{p.Name}}}","description":"{{{p.Name}}} plan","price_in_cents":{{{p.PriceInCents}}},"interval":1,"interval_unit":"month"}}""")) + "]";

    public static string CustomerJson(int id, string reference) =>
        $$$"""{"customer":{"id":{{{id}}},"reference":"{{{reference}}}","email":"{{{reference}}}","first_name":"x","last_name":"y"}}""";

    public static string SubscriptionJson(int id, string reference, string productHandle = "eshop-pro", long priceInCents = 29900) =>
        $$$"""
        {"subscription":{"id":{{{id}}},"state":"active","reference":"{{{reference}}}","currency":"USD",
         "product_price_in_cents":{{{priceInCents}}},"current_period_ends_at":"2026-11-20T10:00:00-05:00",
         "activated_at":"2026-10-20T10:00:00-05:00","created_at":"2026-10-20T10:00:00-05:00",
         "product":{"id":1,"handle":"{{{productHandle}}}","name":"Pro","price_in_cents":{{{priceInCents}}},"interval":1,"interval_unit":"month"} } }
        """;

    public static string SubscriptionListJson(params string[] subscriptionJsons) => "[" + string.Join(",", subscriptionJsons) + "]";
}
