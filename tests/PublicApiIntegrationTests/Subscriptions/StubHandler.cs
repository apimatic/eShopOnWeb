using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.Subscriptions;

/// <summary>
/// Captures every request and answers each from a test-supplied responder. Shared by the
/// Maxio subscription service tests; no real network traffic ever occurs.
/// </summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

    public List<string?> RequestBodies { get; } = new List<string?>();

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (request.Content is not null)
        {
            RequestBodies.Add(request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
        }
        else
        {
            RequestBodies.Add(null);
        }

        return Task.FromResult(_responder(request));
    }
}

/// <summary>Wire-format JSON fixtures for the Maxio Advanced Billing endpoints the service calls.</summary>
public static class StubJson
{
    private const string ProPlanJson = """
        {
          "id": 7126957,
          "name": "Pro Plan",
          "handle": "eshop-pro",
          "price_in_cents": 29900,
          "interval": 1,
          "interval_unit": "month",
          "require_credit_card": false
        }
        """;

    private const string CardRequiredPlanJson = """
        {
          "id": 999,
          "name": "Card Required Plan",
          "handle": "card-plan",
          "price_in_cents": 9900,
          "interval": 1,
          "interval_unit": "month",
          "require_credit_card": true
        }
        """;

    private const string ProductsTemplate = """
        [
          { "product": {PLAN_PRO} },
          { "product": {PLAN_BASIC} }
        ]
        """;

    private const string CustomerResponseJson = """
        {
          "customer": {
            "id": 123,
            "first_name": "demouser",
            "last_name": "microsoft",
            "email": "demouser@microsoft.com",
            "reference": "demouser@microsoft.com"
          }
        }
        """;

    private const string ActiveSubscriptionTemplate = """
        {
          "subscription": {
            "id": 456,
            "state": "active",
            "product": {PLAN_PRO},
            "product_price_in_cents": 29900,
            "currency": "USD",
            "current_period_started_at": "2026-09-01T12:00:00Z",
            "current_period_ends_at": "2026-10-01T12:00:00Z",
            "created_at": "2026-09-01T12:00:00Z"
          }
        }
        """;

    public static string Products() =>
        ProductsTemplate.Replace("{PLAN_PRO}", ProPlanJson).Replace("{PLAN_BASIC}", "null");

    public static string ProductsWithBasic() =>
        ProductsTemplate
            .Replace("{PLAN_PRO}", ProPlanJson)
            .Replace("{PLAN_BASIC}", """
                {
                  "id": 7126958,
                  "name": "Basic Plan",
                  "handle": "basic-plan",
                  "price_in_cents": 2900,
                  "interval": 1,
                  "interval_unit": "month",
                  "require_credit_card": false
                }
                """);

    public static string ProductsWithCardRequiredPlan() =>
        ProductsTemplate
            .Replace("{PLAN_PRO}", ProPlanJson)
            .Replace("{PLAN_BASIC}", CardRequiredPlanJson);

    public static string CustomerResponse() => CustomerResponseJson;

    public static string SubscriptionResponse() =>
        ActiveSubscriptionTemplate.Replace("{PLAN_PRO}", ProPlanJson);

    public static string SubscriptionsArray() => "[ " + SubscriptionResponse() + " ]";

    public static string EmptyArray() => "[]";
}
