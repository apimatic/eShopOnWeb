#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>
/// An in-process stand-in for the Maxio HTTP API: routes by method + path, records every request (and its body,
/// captured while still readable) so tests can count what actually reached the provider.
/// </summary>
public sealed class MaxioStub : HttpMessageHandler
{
    public delegate Task<HttpResponseMessage> Responder(HttpRequestMessage request, string? body);

    private readonly ConcurrentDictionary<string, Responder> _routes = new();

    public ConcurrentQueue<(HttpMethod Method, string Path, string? Body)> Requests { get; } = new();

    public MaxioStub On(HttpMethod method, string path, Responder responder)
    {
        _routes[Key(method, path)] = responder;
        return this;
    }

    public MaxioStub On(HttpMethod method, string path, HttpStatusCode status, string json) =>
        On(method, path, (_, _) => Task.FromResult(Json(status, json)));

    public int Count(HttpMethod method, string path) =>
        Requests.Count(r => r.Method == method && r.Path == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        Requests.Enqueue((request.Method, path, body));
        if (!_routes.TryGetValue(Key(request.Method, path), out var responder))
        {
            return Json(HttpStatusCode.NotFound, """{"errors":["no stub route"]}""");
        }
        var response = await responder(request, body);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Reads <c>subscription.reference</c> from a create-subscription body.</summary>
    public static string? SubscriptionReferenceIn(string? body) =>
        body is null ? null : JsonDocument.Parse(body).RootElement.GetProperty("subscription").GetProperty("reference").GetString();

    public static string ProductsJson(params (string Handle, string Name, long PriceInCents)[] products) =>
        JsonSerializer.Serialize(products.Select((p, i) => new
        {
            product = new
            {
                id = i + 1,
                name = p.Name,
                handle = p.Handle,
                price_in_cents = p.PriceInCents,
                interval = 1,
                interval_unit = "month",
                product_family = new { handle = MaxioTestHarness.FamilyHandle }
            }
        }));

    public static string CustomerJson(int id, string reference) =>
        JsonSerializer.Serialize(new { customer = new { id, reference, email = "demouser@microsoft.com" } });

    public static string SubscriptionJson(int id, string? reference, int customerId, string planHandle, string state = "active") =>
        JsonSerializer.Serialize(new
        {
            subscription = new
            {
                id,
                state,
                reference,
                product_price_in_cents = 29900,
                currency = "USD",
                current_period_ends_at = "2026-11-15T00:00:00Z",
                next_assessment_at = "2026-11-15T00:00:00Z",
                customer = new { id = customerId },
                product = new
                {
                    handle = planHandle,
                    name = "Pro Plan",
                    interval = 1,
                    interval_unit = "month",
                    product_family = new { handle = MaxioTestHarness.FamilyHandle }
                }
            }
        });

    public static string SubscriptionListJson(IEnumerable<string> subscriptionJsons) => "[" + string.Join(",", subscriptionJsons) + "]";

    private static string Key(HttpMethod method, string path) => method.Method + " " + path;
}
