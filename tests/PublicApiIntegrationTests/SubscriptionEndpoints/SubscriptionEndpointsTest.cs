using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private const string NormalUser = "demouser@microsoft.com";

    private static WebApplicationFactory<Program> CreateFactory(StubMaxioHandler handler)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Maxio:ApiKey"] = "test-api-key",
                    ["Maxio:Subdomain"] = "test-site",
                    ["Maxio:ProductFamilyHandle"] = "eshop-subscribe"
                });
            });
            builder.ConfigureServices(services =>
            {
                // Re-register the named Maxio client's primary handler; the last
                // registration wins, so the SDK under test talks to the stub.
                services.AddHttpClient(ConfigureMaxioServices.MaxioHttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(_ => handler);
            });
        });
    }

    private static HttpClient NewAuthorizedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static bool Is(HttpRequestMessage request, HttpMethod method, string pathPart) =>
        request.Method == method && request.RequestUri!.AbsolutePath.Contains(pathPart);

    [TestMethod]
    public async Task ListSubscriptionPlans_ReturnsPlansFromBillingProvider()
    {
        var handler = new StubMaxioHandler(request =>
        {
            if (Is(request, HttpMethod.Get, "products"))
            {
                return StubMaxioHandler.Json(HttpStatusCode.OK, """
                    [
                      { "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "description": "Pro tier", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } },
                      { "product": { "id": 7126958, "name": "Basic Plan", "handle": "basic-plan", "description": "Basic tier", "price_in_cents": 2900, "interval": 1, "interval_unit": "month" } }
                    ]
                    """);
            }
            return StubMaxioHandler.Json(HttpStatusCode.NotFound, "\"not found\"");
        });

        using var factory = CreateFactory(handler);
        var client = NewAuthorizedClient(factory);

        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();

        Assert.AreEqual(2, model!.Plans.Count);
        var pro = model.Plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(29900, pro.PriceInCents);
        Assert.AreEqual(1, pro.Interval);
        Assert.AreEqual("month", pro.IntervalUnit);

        // The product family must be addressed by handle, never by a numeric id
        // (the ':' separator is URL-encoded on the wire).
        StringAssert.Contains(handler.Requests[0].RequestUri!.AbsolutePath, "handle%3Aeshop-subscribe");
    }

    [TestMethod]
    public async Task Subscribe_CreatesCustomerAndSubscription()
    {
        var handler = new StubMaxioHandler(request =>
        {
            if (Is(request, HttpMethod.Get, "lookup"))
            {
                return StubMaxioHandler.Json(HttpStatusCode.NotFound, "\"customer not found\"");
            }
            if (Is(request, HttpMethod.Post, "customers"))
            {
                return StubMaxioHandler.Json(HttpStatusCode.Created,
                    """{ "customer": { "id": 123, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com", "first_name": "demouser", "last_name": "Customer" } }""");
            }
            if (Is(request, HttpMethod.Get, "subscriptions"))
            {
                return StubMaxioHandler.Json(HttpStatusCode.OK, "[]");
            }
            if (Is(request, HttpMethod.Post, "subscriptions"))
            {
                return StubMaxioHandler.Json(HttpStatusCode.Created,
                    """{ "subscription": { "id": 555, "state": "active", "product": { "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900 }, "product_price_in_cents": 29900, "current_period_ends_at": "2026-09-26T00:00:00Z" } }""");
            }
            return StubMaxioHandler.Json(HttpStatusCode.NotFound, "\"not found\"");
        });

        using var factory = CreateFactory(handler);
        var client = NewAuthorizedClient(factory);

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(new CreateSubscriptionRequest { ProductHandle = "eshop-pro" }),
            Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/subscriptions", jsonContent);
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        Assert.AreEqual(555, model!.Subscription!.SubscriptionId);
        Assert.AreEqual("active", model.Subscription.State);
        Assert.AreEqual("eshop-pro", model.Subscription.PlanHandle);
        Assert.AreEqual(29900, model.Subscription.PriceInCents);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero), model.Subscription.NextBillingDate);

        // The customer was created with the user's identity as the unique reference...
        var createCustomerBody = handler.RequestBodies[handler.Requests.FindIndex(r => Is(r, HttpMethod.Post, "customers"))];
        StringAssert.Contains(createCustomerBody, "\"reference\":\"demouser@microsoft.com\"");

        // ...and the subscription identifies the plan by handle, not numeric id.
        var createSubscriptionBody = handler.RequestBodies[handler.Requests.FindIndex(r => Is(r, HttpMethod.Post, "subscriptions"))];
        StringAssert.Contains(createSubscriptionBody, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(createSubscriptionBody, "\"customer_id\":123");
    }

    [TestMethod]
    public async Task Subscribe_WhenActiveSubscriptionExists_ReturnsItWithoutCreatingAnother()
    {
        var handler = new StubMaxioHandler(request =>
        {
            if (Is(request, HttpMethod.Get, "lookup"))
            {
                return StubMaxioHandler.Json(HttpStatusCode.OK,
                    """{ "customer": { "id": 123, "reference": "demouser@microsoft.com" } }""");
            }
            if (Is(request, HttpMethod.Get, "subscriptions"))
            {
                return StubMaxioHandler.Json(HttpStatusCode.OK,
                    """[ { "subscription": { "id": 555, "state": "active", "product": { "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900 }, "current_period_ends_at": "2026-09-26T00:00:00Z" } } ]""");
            }
            return StubMaxioHandler.Json(HttpStatusCode.NotFound, "\"not found\"");
        });

        using var factory = CreateFactory(handler);
        var client = NewAuthorizedClient(factory);

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(new CreateSubscriptionRequest { ProductHandle = "eshop-pro" }),
            Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/subscriptions", jsonContent);
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        Assert.AreEqual(555, model!.Subscription!.SubscriptionId);
        Assert.AreEqual("active", model.Subscription.State);

        // Idempotency: no customer or subscription was created on the second subscribe.
        Assert.IsFalse(handler.Requests.Any(r => r.Method == HttpMethod.Post),
            "A double subscribe must not issue any create call to Maxio.");
    }

    [TestMethod]
    public async Task MySubscriptions_WhenNoBillingCustomer_ReturnsEmptyList()
    {
        var handler = new StubMaxioHandler(request =>
        {
            if (Is(request, HttpMethod.Get, "lookup"))
            {
                return StubMaxioHandler.Json(HttpStatusCode.NotFound, "\"customer not found\"");
            }
            return StubMaxioHandler.Json(HttpStatusCode.NotFound, "\"not found\"");
        });

        using var factory = CreateFactory(handler);
        var client = NewAuthorizedClient(factory);

        var response = await client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();

        Assert.AreEqual(0, model!.Subscriptions.Count);
    }

    [TestMethod]
    public async Task MySubscriptions_ReturnsSubscriptionsForCurrentUser()
    {
        var handler = new StubMaxioHandler(request =>
        {
            if (Is(request, HttpMethod.Get, "lookup"))
            {
                return StubMaxioHandler.Json(HttpStatusCode.OK,
                    """{ "customer": { "id": 123, "reference": "demouser@microsoft.com" } }""");
            }
            if (Is(request, HttpMethod.Get, "subscriptions"))
            {
                return StubMaxioHandler.Json(HttpStatusCode.OK,
                    """[ { "subscription": { "id": 555, "state": "active", "product": { "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900 }, "current_period_ends_at": "2026-09-26T00:00:00Z" } } ]""");
            }
            return StubMaxioHandler.Json(HttpStatusCode.NotFound, "\"not found\"");
        });

        using var factory = CreateFactory(handler);
        var client = NewAuthorizedClient(factory);

        var response = await client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();

        Assert.AreEqual(1, model!.Subscriptions.Count);
        Assert.AreEqual(555, model.Subscriptions[0].SubscriptionId);
        Assert.AreEqual("Pro Plan", model.Subscriptions[0].PlanName);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero), model.Subscriptions[0].NextBillingDate);
    }

    [TestMethod]
    public async Task Subscribe_WhenPlanHandleUnknown_SurfacesProviderRejectionAs422()
    {
        var handler = new StubMaxioHandler(request =>
        {
            if (Is(request, HttpMethod.Get, "lookup"))
            {
                return StubMaxioHandler.Json(HttpStatusCode.OK,
                    """{ "customer": { "id": 123, "reference": "demouser@microsoft.com" } }""");
            }
            if (Is(request, HttpMethod.Get, "subscriptions"))
            {
                return StubMaxioHandler.Json(HttpStatusCode.OK, "[]");
            }
            if (Is(request, HttpMethod.Post, "subscriptions"))
            {
                return StubMaxioHandler.Json(HttpStatusCode.UnprocessableEntity,
                    """{ "errors": ["Product: could not be found."] }""");
            }
            return StubMaxioHandler.Json(HttpStatusCode.NotFound, "\"not found\"");
        });

        using var factory = CreateFactory(handler);
        var client = NewAuthorizedClient(factory);

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(new CreateSubscriptionRequest { ProductHandle = "no-such-plan" }),
            Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/subscriptions", jsonContent);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "could not be found");
    }

    [TestMethod]
    public async Task Endpoints_RequireAJwtBearerToken()
    {
        var handler = new StubMaxioHandler(_ => StubMaxioHandler.Json(HttpStatusCode.OK, "[]"));
        using var factory = CreateFactory(handler);
        var client = factory.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(new CreateSubscriptionRequest { ProductHandle = "eshop-pro" }),
            Encoding.UTF8, "application/json");
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsync("api/subscriptions", jsonContent)).StatusCode);

        Assert.AreEqual(0, handler.Requests.Count, "Unauthenticated calls must never reach Maxio.");
    }
}
