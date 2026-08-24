using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string UserName = "demouser@microsoft.com";
    private const string FamilyHandle = "eshop-subscribe";
    private const string PlanHandle = "eshop-pro";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<HttpRequestMessage> Requests { get; } = new();

        // The SDK disposes request content after sending, so bodies are captured at send time.
        public List<string?> RequestBodies { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responder(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Empty(HttpStatusCode status) => new(status)
    {
        Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
    };

    private static (MaxioSubscriptionBillingService Service, StubHandler Handler) CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var settings = Options.Create(new MaxioSettings { ProductFamilyHandle = FamilyHandle });
        var logger = Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
        return (new MaxioSubscriptionBillingService(client, settings, logger), handler);
    }

    private static string PathOf(HttpRequestMessage request) => request.RequestUri!.AbsolutePath;

    private static string BodyOf(StubHandler handler, string path)
    {
        var index = handler.Requests.FindIndex(r => r.Method == HttpMethod.Post && PathOf(r) == path);
        Assert.True(index >= 0, $"Expected a POST to {path}.");
        return handler.RequestBodies[index] ?? string.Empty;
    }

    [Fact]
    public async Task ListPlans_ReturnsNonArchivedPlansFromConfiguredFamily()
    {
        var (service, handler) = CreateService(request =>
        {
            if (PathOf(request) == "/product_families.json")
            {
                return Json(HttpStatusCode.OK,
                    """[{ "product_family": { "id": 3023074, "name": "eShop Subscribe", "handle": "eshop-subscribe" } }]""");
            }
            if (PathOf(request) == "/product_families/3023074/products.json")
            {
                return Json(HttpStatusCode.OK,
                    """
                    [
                        { "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "archived_at": null } },
                        { "product": { "id": 99, "name": "Retired Plan", "handle": "retired", "price_in_cents": 100, "interval": 1, "interval_unit": "month", "archived_at": "2024-01-01T00:00:00Z" } }
                    ]
                    """);
            }
            return Empty(HttpStatusCode.NotFound);
        });

        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal(PlanHandle, plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
        // The family handle was resolved to its current numeric id, never hard-coded.
        Assert.Contains(handler.Requests, r => PathOf(r) == "/product_families.json");
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerWhenMissing_ThenCreatesSubscription()
    {
        var (service, handler) = CreateService(request =>
        {
            return (request.Method.Method, PathOf(request)) switch
            {
                ("GET", "/subscriptions/lookup.json") => Empty(HttpStatusCode.NotFound),
                ("GET", "/products/handle/eshop-pro.json") => Json(HttpStatusCode.OK,
                    """{ "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900 } }"""),
                ("GET", "/customers/lookup.json") => Empty(HttpStatusCode.NotFound),
                ("POST", "/customers.json") => Json(HttpStatusCode.Created,
                    """{ "customer": { "id": 4242, "reference": "demouser@microsoft.com", "first_name": "Demouser", "last_name": "Account", "email": "demouser@microsoft.com" } }"""),
                ("POST", "/subscriptions.json") => Json(HttpStatusCode.Created,
                    """{ "subscription": { "id": 555, "state": "active", "reference": "demouser@microsoft.com:eshop-pro", "product_price_in_cents": 29900, "currency": "USD", "next_assessment_at": "2026-09-24T00:00:00Z", "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro" } } }"""),
                _ => Empty(HttpStatusCode.NotFound)
            };
        });

        var subscription = await service.SubscribeAsync(UserName, PlanHandle);

        Assert.Equal(555, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("Pro Plan", subscription.ProductName);
        Assert.Equal(PlanHandle, subscription.ProductHandle);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal("USD", subscription.Currency);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingDate);

        // Exactly one customer was created, and the subscription carries the deterministic reference.
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && PathOf(r) == "/customers.json");
        var body = BodyOf(handler, "/subscriptions.json");
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":4242", body);
        Assert.Contains("\"reference\":\"demouser@microsoft.com:eshop-pro\"", body);
    }

    [Fact]
    public async Task Subscribe_WhenSubscriptionWithSameReferenceExists_ReturnsItWithoutCreatingAnything()
    {
        var (service, handler) = CreateService(request =>
        {
            if (request.Method == HttpMethod.Get && PathOf(request) == "/subscriptions/lookup.json")
            {
                return Json(HttpStatusCode.OK,
                    """{ "subscription": { "id": 555, "state": "active", "reference": "demouser@microsoft.com:eshop-pro", "product_price_in_cents": 29900, "current_period_ends_at": "2026-09-24T00:00:00Z", "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro" } } }""");
            }
            return Empty(HttpStatusCode.NotFound);
        });

        var subscription = await service.SubscribeAsync(UserName, PlanHandle);

        Assert.Equal(555, subscription.Id);
        // Double-click safety: no customer or subscription was created.
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Subscribe_WhenCustomerCreateRaces_ReLooksUpCustomerByReference()
    {
        var lookupCalls = 0;
        var (service, handler) = CreateService(request =>
        {
            return (request.Method.Method, PathOf(request)) switch
            {
                ("GET", "/subscriptions/lookup.json") => Empty(HttpStatusCode.NotFound),
                ("GET", "/products/handle/eshop-pro.json") => Json(HttpStatusCode.OK,
                    """{ "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro" } }"""),
                ("GET", "/customers/lookup.json") => ++lookupCalls == 1
                    ? Empty(HttpStatusCode.NotFound)
                    : Json(HttpStatusCode.OK,
                        """{ "customer": { "id": 4242, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" } }"""),
                // A concurrent request won the create race; the provider rejects the duplicate reference.
                ("POST", "/customers.json") => Json(HttpStatusCode.UnprocessableEntity, """{ "errors": {} }"""),
                ("POST", "/subscriptions.json") => Json(HttpStatusCode.Created,
                    """{ "subscription": { "id": 556, "state": "active", "reference": "demouser@microsoft.com:eshop-pro", "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro" } } }"""),
                _ => Empty(HttpStatusCode.NotFound)
            };
        });

        var subscription = await service.SubscribeAsync(UserName, PlanHandle);

        Assert.Equal(556, subscription.Id);
        Assert.Contains("\"customer_id\":4242", BodyOf(handler, "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_WithUnknownPlanHandle_ThrowsNotFoundBillingException()
    {
        var (service, handler) = CreateService(request =>
        {
            return (request.Method.Method, PathOf(request)) switch
            {
                ("GET", "/subscriptions/lookup.json") => Empty(HttpStatusCode.NotFound),
                ("GET", "/products/handle/no-such-plan.json") => Empty(HttpStatusCode.NotFound),
                _ => Empty(HttpStatusCode.NotFound)
            };
        });

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(UserName, "no-such-plan"));

        Assert.Equal((int)HttpStatusCode.NotFound, ex.StatusCode);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ListSubscriptions_WhenUserHasNoCustomer_ReturnsEmpty()
    {
        var (service, _) = CreateService(_ => Empty(HttpStatusCode.NotFound));

        var subscriptions = await service.ListSubscriptionsAsync(UserName);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListSubscriptions_ReturnsMappedSubscriptions()
    {
        var (service, _) = CreateService(request =>
        {
            return (request.Method.Method, PathOf(request)) switch
            {
                ("GET", "/customers/lookup.json") => Json(HttpStatusCode.OK,
                    """{ "customer": { "id": 4242, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" } }"""),
                ("GET", "/customers/4242/subscriptions.json") => Json(HttpStatusCode.OK,
                    """
                    [
                        { "subscription": { "id": 555, "state": "active", "product_price_in_cents": 29900, "next_assessment_at": "2026-09-24T00:00:00Z", "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro" } } },
                        { "subscription": { "id": 556, "state": "canceled", "product_price_in_cents": 2900, "current_period_ends_at": "2026-08-01T00:00:00Z", "product": { "id": 7126958, "name": "Basic Plan", "handle": "basic-plan" } } }
                    ]
                    """),
                _ => Empty(HttpStatusCode.NotFound)
            };
        });

        var subscriptions = await service.ListSubscriptionsAsync(UserName);

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal("active", subscriptions[0].State);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero), subscriptions[0].NextBillingDate);
        Assert.Equal("canceled", subscriptions[1].State);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), subscriptions[1].NextBillingDate);
    }

    [Fact]
    public async Task ListPlans_WhenProviderUnreachable_ThrowsServiceUnavailableBillingException()
    {
        var (service, _) = CreateService(_ => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.ListPlansAsync());

        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }
}
