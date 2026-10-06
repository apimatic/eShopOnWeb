using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.eShopWeb;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Routes requests by verb and path shape rather than exact Maxio URL templates;
/// the live end-to-end verification covers the real wire paths.
/// </summary>
public sealed class StubMaxioHandler : HttpMessageHandler
{
    public const string TestUserId = "demouser@microsoft.com";

    public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

    /// <summary>Bodies captured at send time — request content is disposed after send.</summary>
    public List<(HttpMethod Method, string Path, string Body)> SentRequests { get; } = new List<(HttpMethod, string, string)>();

    /// <summary>True → the user's Maxio customer exists (lookup succeeds); false → 404.</summary>
    public bool CustomerExists { get; set; }

    /// <summary>True → finding a subscription by reference succeeds; false → 404.</summary>
    public bool SubscriptionByReferenceExists { get; set; }

    /// <summary>Subscriptions returned when listing a customer's subscriptions.</summary>
    public int ListedSubscriptionCount { get; set; }

    /// <summary>Product handles that do not exist on the (stub) site → 404.</summary>
    public HashSet<string> MissingProductHandles { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        SentRequests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
        var path = request.RequestUri!.AbsolutePath.ToLowerInvariant();
        var query = request.RequestUri.Query.ToLowerInvariant();
        var response = Route(request.Method, path, query);
        return response;
    }

    public IReadOnlyList<string> SentBodies(HttpMethod method, string pathContains) =>
        SentRequests.Where(r => r.Method == method && r.Path.Contains(pathContains)).Select(r => r.Body).ToList();

    private HttpResponseMessage Route(HttpMethod method, string path, string query)
    {
        if (method == HttpMethod.Get && path.Contains("/products"))
        {
            foreach (var handle in MissingProductHandles)
            {
                if (path.Contains(handle.ToLowerInvariant()) || query.Contains(handle.ToLowerInvariant()))
                {
                    return Json(HttpStatusCode.NotFound, "{}");
                }
            }
            return Json(HttpStatusCode.OK, ProductJson(HandleFrom(path, query, "eshop-pro")));
        }

        if (method == HttpMethod.Get && path.Contains("/customers") && path.Contains("/subscriptions"))
        {
            var items = Enumerable.Range(0, ListedSubscriptionCount)
                .Select(i => SubscriptionJson(5001 + i, "eshop-pro"))
                .ToArray();
            return Json(HttpStatusCode.OK, "[" + string.Join(",", items) + "]");
        }

        if (method == HttpMethod.Get && path.Contains("/customers") && query.Contains("reference="))
        {
            return CustomerExists
                ? Json(HttpStatusCode.OK, CustomerJson())
                : Json(HttpStatusCode.NotFound, "{}");
        }

        if (method == HttpMethod.Get && path.Contains("/subscriptions"))
        {
            return SubscriptionByReferenceExists
                ? Json(HttpStatusCode.OK, SubscriptionJson(5001, "eshop-pro"))
                : Json(HttpStatusCode.NotFound, "");
        }

        if (method == HttpMethod.Post && path.Contains("/customers"))
        {
            CustomerExists = true;
            return Json(HttpStatusCode.OK, CustomerJson());
        }

        if (method == HttpMethod.Post && path.Contains("/subscriptions"))
        {
            SubscriptionByReferenceExists = true;
            return Json(HttpStatusCode.OK, SubscriptionJson(5001, "eshop-pro"));
        }

        return Json(HttpStatusCode.NotFound, "{}");
    }

    private static string HandleFrom(string path, string query, string fallback)
    {
        foreach (var candidate in new[] { "eshop-pro", "basic-plan", "ghost-plan" })
        {
            if (path.Contains(candidate) || query.Contains(candidate))
            {
                return candidate;
            }
        }
        return fallback;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static string CustomerJson() =>
        "{\"customer\": {\"id\": 9001, \"reference\": \"demouser@microsoft.com\", \"email\": \"demouser@microsoft.com\", \"first_name\": \"Demouser\", \"last_name\": \"Shopper\"}}";

    private static string SubscriptionJson(int id, string planHandle) =>
        $"{{\"subscription\": {{\"id\": {id}, \"state\": \"active\", \"reference\": \"{TestUserId}:{planHandle}\", \"next_assessment_at\": \"2026-11-06T00:00:00Z\", \"current_billing_amount_in_cents\": 29900, \"created_at\": \"2026-10-06T12:00:00Z\", \"product\": {{\"id\": 7001, \"handle\": \"{planHandle}\", \"name\": \"eShop {planHandle}\", \"price_in_cents\": 29900}}}}}}";

    private static string ProductJson(string handle) =>
        $"{{\"product\": {{\"id\": 7001, \"handle\": \"{handle}\", \"name\": \"eShop {handle}\", \"price_in_cents\": 29900}}}}";
}

[TestClass]
public class SubscriptionEndpointsTest
{
    private WebApplicationFactory<Program> _factory = null!;
    private StubMaxioHandler _stub = null!;

    [TestInitialize]
    public void Initialize()
    {
        _stub = new StubMaxioHandler();
        RebuildFactory();
    }

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    private void RebuildFactory(Dictionary<string, string?>? settings = null)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            if (settings is not null)
            {
                foreach (var (key, value) in settings)
                {
                    builder.UseSetting(key, value);
                }
            }
            builder.ConfigureTestServices(services =>
                services.AddHttpClient("Maxio")
                    .ConfigurePrimaryHttpMessageHandler(() => _stub));
        });
    }

    private HttpClient Client(string token) => _factory.CreateClient().WithAuth(token);

    [TestMethod]
    public async Task SubscriptionEndpointsRequireAuthentication()
    {
        var client = _factory.CreateClient();

        var plans = await client.GetAsync("api/subscription-plans");
        var mine = await client.GetAsync("api/my-subscriptions");
        var subscribe = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, mine.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscribe.StatusCode);
        Assert.AreEqual(0, _stub.Requests.Count, "no Maxio call may happen for unauthenticated requests");
    }

    [TestMethod]
    public async Task PlansAreListedWithLivePrices()
    {
        var response = await Client(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        StringAssert.Contains(body, "\"handle\":\"eshop-pro\"");
        StringAssert.Contains(body, "\"priceInCents\":29900");
        StringAssert.Contains(body, "\"available\":true");
        StringAssert.Contains(body, "\"isDefault\":true");
    }

    [TestMethod]
    public async Task PlansMissingOnTheSiteAreMarkedUnavailable()
    {
        _stub.MissingProductHandles.Add("ghost-plan");
        RebuildFactory(new Dictionary<string, string?>
        {
            ["Maxio:Plans:2:Handle"] = "ghost-plan",
            ["Maxio:Plans:2:DisplayName"] = "Ghost Plan",
            ["Maxio:Plans:2:IsDefault"] = "false"
        });

        var response = await Client(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        StringAssert.Contains(body, "\"handle\":\"ghost-plan\"");
        StringAssert.Contains(body, "\"available\":false");
    }

    [TestMethod]
    public async Task SubscribeCreatesCustomerAndSubscription()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "\"planHandle\":\"eshop-pro\"");
        StringAssert.Contains(body, "\"planResolved\":true");
        StringAssert.Contains(body, "\"state\":\"active\"");
        StringAssert.Contains(body, "\"priceInCents\":29900");
        StringAssert.Contains(body, "\"nextBillingDate\":");

        var createRequests = _stub.SentBodies(HttpMethod.Post, "/subscriptions");
        Assert.AreEqual(1, createRequests.Count);
        var sentJson = createRequests[0];
        StringAssert.Contains(sentJson, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(sentJson, "\"customer_id\":9001");
        StringAssert.Contains(sentJson, $"\"reference\":\"{StubMaxioHandler.TestUserId}:eshop-pro\"");
    }

    [TestMethod]
    public async Task RepeatedSubscribeReturnsTheExistingSubscription()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var first = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });
        var second = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);

        var subscriptionCreates = _stub.Requests.Count(
            r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/subscriptions"));
        Assert.AreEqual(1, subscriptionCreates, "a double-click must not create a second subscription");
    }

    [TestMethod]
    public async Task SubscribeToUnknownPlanIsRejectedWithoutCallingMaxio()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "not-a-plan" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, _stub.Requests.Count);
    }

    [TestMethod]
    public async Task MySubscriptionsAreEmptyWhenTheUserNeverSubscribed()
    {
        var response = await Client(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/my-subscriptions");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "\"subscriptions\":[]");
        Assert.IsFalse(_stub.Requests.Any(r => r.Method == HttpMethod.Post),
            "listing must not create a Maxio customer");
    }

    [TestMethod]
    public async Task MySubscriptionsListsTheUsersMaxioSubscriptions()
    {
        _stub.CustomerExists = true;
        _stub.ListedSubscriptionCount = 2;

        var response = await Client(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/my-subscriptions");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "\"subscriptionId\":5001");
        StringAssert.Contains(body, "\"subscriptionId\":5002");
        StringAssert.Contains(body, "\"planHandle\":\"eshop-pro\"");
    }
}

internal static class SubscriptionTestHttpClientExtensions
{
    public static HttpClient WithAuth(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}