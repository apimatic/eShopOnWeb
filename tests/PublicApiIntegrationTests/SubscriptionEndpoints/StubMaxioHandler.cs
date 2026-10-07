using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// A stand-in for the Maxio Advanced Billing API, wired behind the named
/// HttpClient the SDK client uses. Stateful across requests within a test so
/// the ensure-customer and find-or-create-subscription flows behave like the
/// real provider (lookup miss → create → lookup hit).
/// </summary>
public sealed class StubMaxioHandler : HttpMessageHandler
{
    public const string FamilyHandle = "eshop-subscribe";
    public const string FamilyId = "42";
    public const string ProHandle = "eshop-pro";
    public const string BasicHandle = "basic-plan";
    public const int ProPriceCents = 29900;
    public const string CustomerReference = "demouser@microsoft.com";
    public const int CustomerId = 1001;
    public const int SubscriptionId = 9001;

    public List<HttpRequestMessage> Requests { get; } = new();

    public int CustomerCreateCount => Requests.Count(r =>
        r.Method == HttpMethod.Post && PathOf(r).Contains("/customers.json"));
    public int SubscriptionCreateCount => Requests.Count(r =>
        r.Method == HttpMethod.Post && PathOf(r).Contains("/subscriptions.json"));

    public bool CustomerExists { get; set; }
    public bool SubscriptionExists { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var path = PathOf(request).ToLowerInvariant();

        if (request.Method == HttpMethod.Get && path.Contains("/product_families") && path.Contains("/products"))
        {
            return Respond(HttpStatusCode.OK, JsonArray(
                ProductJson(ProHandle, "eShop Pro", ProPriceCents),
                ProductJson(BasicHandle, "eShop Basic", 2900)));
        }
        if (request.Method == HttpMethod.Get && path.Contains("/product_families"))
        {
            return Respond(HttpStatusCode.OK, JsonArray(
                "{\"product_family\":{\"id\":" + FamilyId + ",\"handle\":\"" + FamilyHandle + "\",\"name\":\"eShop Subscribe\"}}"));
        }
        if (request.Method == HttpMethod.Get && path.Contains("/products/handle/"))
        {
            var handle = path.Split("/products/handle/")[1].Split(".json")[0];
            return handle switch
            {
                ProHandle => Respond(HttpStatusCode.OK, ProductJson(ProHandle, "eShop Pro", ProPriceCents)),
                BasicHandle => Respond(HttpStatusCode.OK, ProductJson(BasicHandle, "eShop Basic", 2900)),
                _ => Respond(HttpStatusCode.NotFound, "")
            };
        }
        if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
        {
            return CustomerExists
                ? Respond(HttpStatusCode.OK, CustomerJson())
                : Respond(HttpStatusCode.NotFound, "");
        }
        if (request.Method == HttpMethod.Post && path.Contains("/customers.json"))
        {
            CustomerExists = true;
            return Respond(HttpStatusCode.Created, CustomerJson());
        }
        if (request.Method == HttpMethod.Get && path.Contains("/subscriptions/lookup"))
        {
            return SubscriptionExists
                ? Respond(HttpStatusCode.OK, SubscriptionJson())
                : Respond(HttpStatusCode.NotFound, "");
        }
        if (request.Method == HttpMethod.Post && path.Contains("/subscriptions.json"))
        {
            SubscriptionExists = true;
            return Respond(HttpStatusCode.Created, SubscriptionJson());
        }
        if (request.Method == HttpMethod.Get && path.Contains("/customers/") && path.Contains("/subscriptions"))
        {
            return Respond(HttpStatusCode.OK, SubscriptionExists ? JsonArray(SubscriptionJson()) : "[]");
        }

        return Respond(HttpStatusCode.NotFound, "");
    }

    private static string PathOf(HttpRequestMessage request) =>
        request.RequestUri?.PathAndQuery ?? string.Empty;

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string json)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }

    private static string JsonArray(params string[] items) => "[" + string.Join(",", items) + "]";

    private static string ProductJson(string handle, string name, long priceCents) =>
        "{\"product\":{\"id\":" + Math.Abs(handle.GetHashCode()) +
        ",\"handle\":\"" + handle +
        "\",\"name\":\"" + name +
        "\",\"description\":\"The " + name + " plan" +
        "\",\"price_in_cents\":" + priceCents +
        ",\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false,\"taxable\":false" +
        ",\"product_family\":{\"id\":" + FamilyId + ",\"handle\":\"" + FamilyHandle + "\"}}}";

    private static string CustomerJson() =>
        "{\"customer\":{\"id\":" + CustomerId +
        ",\"reference\":\"" + CustomerReference +
        "\",\"email\":\"" + CustomerReference +
        "\",\"first_name\":\"demouser\",\"last_name\":\"microsoft.com\"}}";

    private static string SubscriptionJson() =>
        "{\"subscription\":{\"id\":" + SubscriptionId +
        ",\"state\":\"active\"" +
        ",\"reference\":\"eshopweb:" + CustomerReference + ":" + ProHandle + "\"" +
        ",\"current_period_ends_at\":\"2026-11-07T12:00:00Z\"" +
        ",\"activated_at\":\"2026-10-07T12:00:00Z\"" +
        ",\"created_at\":\"2026-10-07T12:00:00Z\"" +
        ",\"product_price_in_cents\":" + ProPriceCents +
        ",\"current_billing_amount_in_cents\":" + ProPriceCents +
        ",\"product\":{\"id\":7001,\"handle\":\"" + ProHandle + "\",\"name\":\"eShop Pro\",\"price_in_cents\":" + ProPriceCents + "}" +
        ",\"customer\":{\"id\":" + CustomerId + ",\"reference\":\"" + CustomerReference + "\"}}}";
}
