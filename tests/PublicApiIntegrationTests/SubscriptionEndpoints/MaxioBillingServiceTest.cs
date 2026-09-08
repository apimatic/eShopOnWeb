using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Exercises MaxioBillingService against a stubbed Maxio HTTP endpoint, verifying the
/// find-or-create idempotency contract without touching the live sandbox.
/// </summary>
[TestClass]
public class MaxioBillingServiceTest
{
    private const string PlansJson =
        """
        [
          {
            "product": {
              "handle": "eshop-pro",
              "name": "Pro Plan",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month",
              "require_credit_card": false,
              "request_credit_card": false,
              "archived_at": null
            }
          },
          {
            "product": {
              "handle": "basic-plan",
              "name": "Basic Plan",
              "price_in_cents": 2900,
              "interval": 1,
              "interval_unit": "month",
              "require_credit_card": false,
              "request_credit_card": false,
              "archived_at": null
            }
          }
        ]
        """;

    // The SDK's subscription deserializer only tolerates compact JSON (whitespace between tokens is
    // not skipped and silently nulls the parsed object), so this stub body is intentionally single-line.
    private const string SubscriptionJson =
        "{\"subscription\":{\"id\":2000,\"state\":\"active\",\"reference\":\"__REFERENCE__\",\"product_price_in_cents\":29900,\"current_period_started_at\":\"2026-09-09T03:32:09+05:00\",\"next_assessment_at\":\"2026-10-09T03:32:09+05:00\",\"created_at\":\"2026-09-09T03:32:09+05:00\",\"product\":{\"handle\":\"eshop-pro\",\"name\":\"Pro Plan\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}}}";

    [TestMethod]
    public async Task ListsPlansFromTheProductFamily()
    {
        var service = CreateService(new MaxioStubHandler(PlansJson));

        var plans = await service.ListSubscriptionPlansAsync();

        Assert.AreEqual(2, plans.Count);
        Assert.AreEqual("eshop-pro", plans[0].PlanHandle);
        Assert.AreEqual("Pro Plan", plans[0].Name);
        Assert.AreEqual(299m, plans[0].Price);
        Assert.AreEqual("month", plans[0].IntervalUnit);
        Assert.IsFalse(plans[0].RequiresCreditCard);
        Assert.AreEqual("basic-plan", plans[1].PlanHandle);
        Assert.AreEqual(29m, plans[1].Price);
    }

    [TestMethod]
    public async Task SubscribeCreatesSubscriptionOnFirstCall()
    {
        var handler = new MaxioStubHandler(PlansJson);
        var service = CreateService(handler);

        var result = await service.SubscribeAsync("shopper@example.com", "eshop-pro", "Ada", "Lovelace");

        Assert.IsTrue(result.Created);
        Assert.AreEqual("eshop-pro", result.Subscription.PlanHandle);
        Assert.AreEqual("Pro Plan", result.Subscription.PlanName);
        Assert.AreEqual(299m, result.Subscription.Price);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.AreEqual(1, handler.CountPostsTo("subscriptions.json"));
        Assert.AreEqual(1, handler.CountPostsTo("customers.json"));
    }

    [TestMethod]
    public async Task SubscribeIsIdempotentForASecondCall()
    {
        var handler = new MaxioStubHandler(PlansJson);
        var service = CreateService(handler);

        var first = await service.SubscribeAsync("shopper@example.com", "eshop-pro", "Ada", "Lovelace");
        var second = await service.SubscribeAsync("shopper@example.com", "eshop-pro", "Ada", "Lovelace");

        Assert.IsTrue(first.Created);
        Assert.IsFalse(second.Created);
        Assert.AreEqual(first.Subscription.SubscriptionId, second.Subscription.SubscriptionId);
        Assert.AreEqual(1, handler.CountPostsTo("subscriptions.json"));
        Assert.AreEqual(1, handler.CountPostsTo("customers.json"));
    }

    [TestMethod]
    public async Task SubscribeConflictsWhenAlreadyOnADifferentPlan()
    {
        var service = CreateService(new MaxioStubHandler(PlansJson));

        await service.SubscribeAsync("shopper@example.com", "eshop-pro", "Ada", "Lovelace");

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(() =>
            service.SubscribeAsync("shopper@example.com", "basic-plan", "Ada", "Lovelace"));

        Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresAPlanHandle()
    {
        var service = CreateService(new MaxioStubHandler(PlansJson));

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(() =>
            service.SubscribeAsync("shopper@example.com", "  ", "Ada", "Lovelace"));

        Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    private static MaxioBillingService CreateService(MaxioStubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(
            new HttpClient(handler),
            new MaxioAdvancedBillingClientOptions());

        return new MaxioBillingService(
            client,
            Options.Create(new MaxioOptions { ProductFamilyHandle = "eshop-subscribe" }),
            new MaxioWriteGuardHandler(),
            new HttpContextAccessor(),
            NullLogger<MaxioBillingService>.Instance);
    }

    private sealed class MaxioStubHandler : HttpMessageHandler
    {
        private readonly string _plansJson;
        private readonly List<string> _requests = new();
        private readonly HashSet<string> _createdCustomers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _subscriptionsByReference = new(StringComparer.Ordinal);
        private int _nextCustomerId = 1000;

        public MaxioStubHandler(string plansJson)
        {
            _plansJson = plansJson;
        }

        public int CountPostsTo(string pathSegment) =>
            _requests.Count(r =>
                r.StartsWith("POST ", StringComparison.Ordinal) &&
                r.Contains(pathSegment, StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var target = $"{request.Method} {request.RequestUri!.AbsolutePath}{request.RequestUri.Query}";
            _requests.Add(target);
            return Task.FromResult(Respond(request, target));
        }

        private HttpResponseMessage Respond(HttpRequestMessage request, string target)
        {
            var path = request.RequestUri!.AbsolutePath;
            var reference = GetQueryValue(request.RequestUri, "reference");

            if (path.Contains("product_families", StringComparison.Ordinal) &&
                path.Contains("products.json", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, _plansJson);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("customers/lookup.json", StringComparison.Ordinal))
            {
                return reference is not null && _createdCustomers.Contains(reference)
                    ? Json(HttpStatusCode.OK, $"{{\"customer\":{{\"id\":{_nextCustomerId},\"reference\":\"{reference}\"}}}}")
                    : Json(HttpStatusCode.NotFound, "");
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("customers.json", StringComparison.Ordinal))
            {
                var createdId = _nextCustomerId++;
                var referenceFromBody = ReadReference(request);
                if (referenceFromBody is not null)
                {
                    _createdCustomers.Add(referenceFromBody);
                }

                return Json(HttpStatusCode.OK,
                    $"{{\"customer\":{{\"id\":{createdId},\"reference\":\"{referenceFromBody}\",\"first_name\":\"Ada\",\"last_name\":\"Lovelace\",\"email\":\"shopper@example.com\"}}}}");
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("subscriptions/lookup.json", StringComparison.Ordinal))
            {
                return reference is not null && _subscriptionsByReference.TryGetValue(reference, out var existing)
                    ? Json(HttpStatusCode.OK, existing)
                    : Json(HttpStatusCode.NotFound, "");
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("subscriptions.json", StringComparison.Ordinal))
            {
                var subscriptionReference = ReadReference(request);
                var created = SubscriptionJson.Replace("__REFERENCE__", subscriptionReference ?? "eshop-subscription-test");
                _subscriptionsByReference[subscriptionReference ?? "eshop-subscription-test"] = created;
                return Json(HttpStatusCode.Created, created);
            }

            return Json(HttpStatusCode.NotFound, "");
        }

        private static string? ReadReference(HttpRequestMessage request)
        {
            if (request.Content is null)
            {
                return null;
            }

            var body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            const string marker = "\"reference\":\"";
            var index = body.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            var start = index + marker.Length;
            var end = body.IndexOf('"', start);
            return end < 0 ? null : body.Substring(start, end - start);
        }

        private static string? GetQueryValue(Uri uri, string key)
        {
            var query = uri.Query;
            if (string.IsNullOrEmpty(query))
            {
                return null;
            }

            foreach (var part in query.TrimStart('?').Split('&'))
            {
                var pieces = part.Split('=');
                if (pieces.Length == 2 && string.Equals(pieces[0], key, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(pieces[1]);
                }
            }

            return null;
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
