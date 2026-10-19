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
/// An in-process stand-in for the Maxio API, plugged in as the SDK's HttpClient primary handler.
/// Default behaviour models a site with two plans and no existing customer; each route can be overridden.
/// </summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    public const int CustomerId = 4242;
    public const int SubscriptionId = 9001;

    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    public IReadOnlyList<RecordedRequest> Requests => _requests.ToArray();

    public int Count(HttpMethod method, string pathSuffix) =>
        _requests.Count(r => r.Method == method && r.Path.EndsWith(pathSuffix, StringComparison.Ordinal));

    /// <summary>Products returned for a page number; default two plans on page 1.</summary>
    public Func<int, string> ProductsPage { get; set; } = page => page == 1
        ? Json(new object[] { Product(1, "pro", "Pro Plan", 29900), Product(2, "basic", "Basic Plan", 2900) })
        : "[]";

    public Func<HttpRequestMessage, Task<HttpResponseMessage>>? OnCustomerLookup { get; set; }
    public Func<HttpRequestMessage, string?, Task<HttpResponseMessage>>? OnCreateCustomer { get; set; }
    public Func<HttpRequestMessage, Task<HttpResponseMessage>>? OnListCustomerSubscriptions { get; set; }
    public Func<HttpRequestMessage, string?, Task<HttpResponseMessage>>? OnCreateSubscription { get; set; }
    public Func<HttpRequestMessage, Task<HttpResponseMessage>>? OnFindSubscription { get; set; }

    /// <summary>Subscriptions the fake currently holds for <see cref="CustomerId"/>.</summary>
    public List<object> Subscriptions { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Read the body now: the SDK disposes request content after the attempt.
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        _requests.Enqueue(new RecordedRequest(request.Method, path, request.RequestUri.Query, body,
            request.Headers.Authorization?.ToString()));

        HttpResponseMessage response;
        if (request.Method == HttpMethod.Get && path.StartsWith("/product_families/", StringComparison.Ordinal) && path.EndsWith("/products.json", StringComparison.Ordinal))
        {
            var page = int.Parse(System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["page"] ?? "1");
            response = Ok(ProductsPage(page));
        }
        else if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
        {
            response = OnCustomerLookup is null ? Status(HttpStatusCode.NotFound, "") : await OnCustomerLookup(request);
        }
        else if (request.Method == HttpMethod.Post && path == "/customers.json")
        {
            response = OnCreateCustomer is null ? Ok(CustomerJson()) : await OnCreateCustomer(request, body);
        }
        else if (request.Method == HttpMethod.Get && path == $"/customers/{CustomerId}/subscriptions.json")
        {
            response = OnListCustomerSubscriptions is null
                ? Ok(Json(Subscriptions.Select(s => new { subscription = s }).ToArray()))
                : await OnListCustomerSubscriptions(request);
        }
        else if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
        {
            if (OnCreateSubscription is not null)
            {
                response = await OnCreateSubscription(request, body);
            }
            else
            {
                var sub = SubscriptionFrom(body);
                Subscriptions.Add(sub);
                response = Ok(Json(new { subscription = sub }));
            }
        }
        else if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
        {
            response = OnFindSubscription is null ? Status(HttpStatusCode.NotFound, "") : await OnFindSubscription(request);
        }
        else
        {
            response = Status(HttpStatusCode.NotFound, "{\"errors\":[\"unrouted\"]}");
        }

        response.RequestMessage = request;
        return response;
    }

    public static object Product(int id, string handle, string name, long priceInCents, bool requireCreditCard = false, string? archivedAt = null) => new
    {
        product = new
        {
            id,
            handle,
            name,
            price_in_cents = priceInCents,
            interval = 1,
            interval_unit = "month",
            require_credit_card = requireCreditCard,
            archived_at = archivedAt
        }
    };

    public static string CustomerJson(string reference = "eshop-demo") =>
        Json(new { customer = new { id = CustomerId, reference, email = "demo@example.com", first_name = "demo", last_name = "eShop" } });

    /// <summary>Builds the subscription Maxio would return for a create body.</summary>
    public static object SubscriptionFrom(string? createBody, int id = SubscriptionId)
    {
        using var doc = JsonDocument.Parse(createBody!);
        var s = doc.RootElement.GetProperty("subscription");
        var handle = s.GetProperty("product_handle").GetString();
        return SubscriptionObject(id, s.GetProperty("reference").GetString()!, handle!, "active");
    }

    public static object SubscriptionObject(int id, string reference, string handle, string state) => new
    {
        id,
        state,
        reference,
        currency = "USD",
        product_price_in_cents = handle == "pro" ? 29900 : 2900,
        current_period_ends_at = "2026-11-19T00:00:00Z",
        created_at = "2026-10-19T00:00:00Z",
        product = new { id = handle == "pro" ? 1 : 2, handle, name = handle == "pro" ? "Pro Plan" : "Basic Plan" }
    };

    public static string Json(object value) => JsonSerializer.Serialize(value);

    public static HttpResponseMessage Ok(string json) => Status(HttpStatusCode.OK, json);

    public static HttpResponseMessage Status(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public record RecordedRequest(HttpMethod Method, string Path, string Query, string? Body, string? Authorization);
