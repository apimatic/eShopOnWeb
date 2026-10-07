using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.MaxioBilling;

/// <summary>
/// Routes fake Maxio Advanced Billing API responses for tests, and records
/// every request so tests can assert on sends and count writes.
/// </summary>
public sealed class FakeMaxioHandler : HttpMessageHandler
{
    public sealed record RecordedRequest(string Method, string PathAndQuery, string? Body);

    public List<HttpRequestMessage> Requests { get; } = new();

    /// <summary>
    /// Request bodies are captured at send time — the SDK disposes request
    /// content once the response arrives.
    /// </summary>
    public List<RecordedRequest> Recorded { get; } = new();

    public int CustomerPosts { get; private set; }
    public int SubscriptionPosts { get; private set; }
    public int CustomerIdToReturn { get; set; } = 42;
    public int SubscriptionIdToReturn { get; set; } = 9001;
    public bool CustomerExists { get; set; }
    public bool SubscriptionExists { get; set; }
    public bool FailSubscriptionCreateWithTransportError { get; set; }

    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();

    public FakeMaxioHandler()
    {
        Route("GET", "/site.json", _ => Json(HttpStatusCode.OK,
            new { site = new { relationship_invoicing_enabled = true } }));

        Route("GET", "/products.json", _ => Json(HttpStatusCode.OK, new[]
        {
            BuildProduct("eshop-pro", "Pro Plan", 29900, "eshop-subscribe"),
            BuildProduct("other-plan", "Other Family Plan", 100, "some-other-family")
        }));

        Route("GET", "/products/handle/eshop-pro.json", _ => Json(HttpStatusCode.OK,
            BuildProduct("eshop-pro", "Pro Plan", 29900, "eshop-subscribe")));

        Route("GET", "/products/handle/does-not-exist.json", _ => Raw(HttpStatusCode.NotFound, "{}"));

        Route("GET", "/customers/lookup.json", _ => CustomerExists
            ? Json(HttpStatusCode.OK, BuildCustomer())
            : Raw(HttpStatusCode.NotFound, "{}"));

        Route("POST", "/customers.json", _ =>
        {
            CustomerPosts++;
            CustomerExists = true;
            return Json(HttpStatusCode.Created, BuildCustomer());
        });

        Route("GET", "/subscriptions/lookup.json", _ => SubscriptionExists
            ? Json(HttpStatusCode.OK, BuildSubscription("active"))
            : Raw(HttpStatusCode.NotFound, "{}"));

        Route("POST", "/subscriptions.json", _ =>
        {
            SubscriptionPosts++;
            if (FailSubscriptionCreateWithTransportError)
            {
                throw new HttpRequestException("connection reset");
            }

            SubscriptionExists = true;
            return Json(HttpStatusCode.Created, BuildSubscription("active"));
        });

        Route("GET", $"/customers/{CustomerIdToReturn}/subscriptions.json", _ => Json(HttpStatusCode.OK, new[]
        {
            BuildSubscription("active")
        }));
    }

    /// <summary>Adds or replaces the response for "{method} {path}".</summary>
    public void Route(string method, string path, Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _routes[$"{method} {path}"] = responder;

    public int CountRequests(string method, string pathFragment) =>
        Requests.Count(r => r.Method == new HttpMethod(method) && r.RequestUri!.PathAndQuery.Contains(pathFragment));

    public object BuildCustomer() => new
    {
        customer = new
        {
            id = CustomerIdToReturn,
            reference = "any-customer-reference",
            first_name = "Demo",
            last_name = "User",
            email = "demouser@microsoft.com"
        }
    };

    public object BuildProduct(string handle, string name, long priceInCents, string familyHandle) => new
    {
        product = new
        {
            handle,
            name,
            price_in_cents = priceInCents,
            interval = 1,
            interval_unit = "month",
            product_price_point_id = 555,
            product_price_point_handle = "base",
            require_credit_card = false,
            product_family = new { id = 7, handle = familyHandle }
        }
    };

    public object BuildSubscription(string state) => new
    {
        subscription = new
        {
            id = SubscriptionIdToReturn,
            state,
            reference = "any-subscription-reference",
            next_assessment_at = "2026-11-07T18:21:08+05:00",
            current_period_ends_at = "2026-11-07T18:21:08+05:00",
            product_price_in_cents = 29900,
            product = new
            {
                handle = "eshop-pro",
                name = "Pro Plan",
                price_in_cents = 29900,
                interval = 1,
                interval_unit = "month",
                product_price_point_id = 555
            }
        }
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Recorded.Add(new RecordedRequest(
            request.Method.Method,
            request.RequestUri!.PathAndQuery,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

        var key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
        if (_routes.TryGetValue(key, out var responder))
        {
            return responder(request);
        }

        return Raw(HttpStatusCode.NotImplemented, $"unmatched fake route: {key}");
    }

    public static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        Raw(status, JsonSerializer.Serialize(body));

    public static HttpResponseMessage Raw(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
}
