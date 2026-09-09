using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>
/// Records every request and answers from a configurable scenario, mimicking the
/// Maxio Advanced Billing wire shapes (envelope-wrapped records).
/// </summary>
public sealed class StubMaxioHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();

    public string FamiliesJson { get; set; } = "[]";
    public string ProductsJson { get; set; } = "[]";
    public string CustomerJson { get; set; } =
        """{"customer":{"id":55,"first_name":"Demo","last_name":"User","email":"demouser@microsoft.com","reference":"user-1"}}""";
    public bool CustomerExists { get; set; }
    public string CustomerSubscriptionsJson { get; set; } = "[]";
    public string CreatedSubscriptionJson { get; set; } =
        """{"subscription":{"id":777,"state":"active","product_id":10,"product_price_in_cents":29900,"next_assessment_at":"2026-10-09T00:00:00Z","current_period_ends_at":"2026-10-09T00:00:00Z","current_period_started_at":"2026-09-09T00:00:00Z","reference":"user-1:eshop-pro","product":{"id":10,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}}}""";

    public int CustomerCreateCount { get; private set; }
    public int SubscriptionCreateCount { get; private set; }

    private readonly Func<HttpRequestMessage, HttpResponseMessage>? _customResponder;

    public StubMaxioHandler(Func<HttpRequestMessage, HttpResponseMessage>? customResponder = null)
    {
        _customResponder = customResponder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        if (_customResponder is not null)
        {
            return Task.FromResult(_customResponder(request));
        }
        return Task.FromResult(Route(request));
    }

    private HttpResponseMessage Route(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var method = request.Method;

        if (method == HttpMethod.Get && path.Contains("/products.json"))
        {
            return Json(HttpStatusCode.OK, ProductsJson);
        }
        if (method == HttpMethod.Get && path.Contains("/product_families"))
        {
            return Json(HttpStatusCode.OK, FamiliesJson);
        }
        if (method == HttpMethod.Get && path.Contains("/customers") && path.Contains("/subscriptions"))
        {
            return Json(HttpStatusCode.OK, CustomerSubscriptionsJson);
        }
        if (method == HttpMethod.Get && path.Contains("/customers"))
        {
            return CustomerExists
                ? Json(HttpStatusCode.OK, CustomerJson)
                : Json(HttpStatusCode.NotFound, "{}");
        }
        if (method == HttpMethod.Post && path.Contains("/customers"))
        {
            CustomerCreateCount++;
            CustomerExists = true;
            return Json(HttpStatusCode.Created, CustomerJson);
        }
        if (method == HttpMethod.Post && path.Contains("/subscriptions"))
        {
            SubscriptionCreateCount++;
            var created = Json(HttpStatusCode.Created, CreatedSubscriptionJson);
            // After a successful create, the customer's subscription list includes it.
            var subscriptionJson = CreatedSubscriptionJson;
            CustomerSubscriptionsJson = CustomerSubscriptionsJson == "[]"
                ? $"[{subscriptionJson}]"
                : CustomerSubscriptionsJson.Insert(CustomerSubscriptionsJson.Length - 1, $", {subscriptionJson}");
            return created;
        }

        return Json(HttpStatusCode.InternalServerError, "{}");
    }

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    public int CountRequests(HttpMethod method, string pathPart) =>
        Requests.Count(r => r.Method == method && r.RequestUri!.AbsolutePath.Contains(pathPart));
}
