using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTests
{
    private const string Plans = "api/subscription-plans";
    private const string Subscriptions = "api/subscriptions";
    private const string MySubscriptions = "api/my-subscriptions";

    private PublicApiTestFactory _factory = null!;
    private HttpClient _client = null!;

    private FakeMaxio Maxio => _factory.Maxio;

    [TestInitialize]
    public async Task Initialize()
    {
        _factory = new PublicApiTestFactory();
        _client = _factory.CreateClient();
        await _factory.ResetEnrollmentsAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task ListPlansReturnsOfferedPlansOfConfiguredFamily()
    {
        var response = await SendAsync(HttpMethod.Get, Plans);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        var handles = body.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("handle").GetString()).ToList();
        CollectionAssert.AreEquivalent(new[] { "basic-plan", "eshop-pro" }, handles); // archived plan left out
        var pro = body.GetProperty("plans").EnumerateArray().Single(p => p.GetProperty("handle").GetString() == "eshop-pro");
        Assert.AreEqual(29900, pro.GetProperty("priceInCents").GetInt64());
        Assert.AreEqual(299m, pro.GetProperty("price").GetDecimal());
        Assert.AreEqual("USD", pro.GetProperty("currency").GetString());
        Assert.AreEqual("month", pro.GetProperty("intervalUnit").GetString());
        Assert.IsFalse(body.GetProperty("truncated").GetBoolean());

        var listCall = Maxio.Requests.Single(r => r.Path.EndsWith("/products.json"));
        Assert.AreEqual("/product_families/handle:" + FakeMaxio.FamilyHandle + "/products.json", listCall.Path);
        Assert.AreEqual("200", listCall.Query["per_page"]);
        Assert.AreEqual("test-site.chargify.com", listCall.Host);
        Assert.AreEqual("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("test-api-key:x")), listCall.Authorization);
    }

    [TestMethod]
    public async Task EndpointsRequireABearerToken()
    {
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, Plans, token: null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Post, Subscriptions, new { planHandle = "eshop-pro" }, token: null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, MySubscriptions, token: null)).StatusCode);
        Assert.AreEqual(0, Maxio.Requests.Count);
    }

    [TestMethod]
    public async Task SubscribeCreatesCustomerAndSubscriptionAndShowsItInMySubscriptions()
    {
        var response = await SubscribeAsync("eshop-pro");

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.AreEqual("Created", body.GetProperty("outcome").GetString());
        var subscription = body.GetProperty("subscription");
        Assert.AreEqual("eshop-pro", subscription.GetProperty("planHandle").GetString());
        Assert.AreEqual("active", subscription.GetProperty("state").GetString());
        Assert.AreEqual(29900, subscription.GetProperty("priceInCents").GetInt64());
        Assert.AreEqual(DateTimeOffset.Parse("2026-11-15T10:00:00+00:00"), subscription.GetProperty("nextBillingAt").GetDateTimeOffset());

        var customer = JsonDocument.Parse(Maxio.Requests.Single(r => r.Method == "POST" && r.Path == "/customers.json").Body!)
            .RootElement.GetProperty("customer");
        Assert.AreEqual("demouser@microsoft.com", customer.GetProperty("email").GetString());
        Assert.AreEqual("demouser", customer.GetProperty("first_name").GetString());
        StringAssert.StartsWith(customer.GetProperty("reference").GetString(), "eshop-user-");
        Assert.IsFalse(customer.GetProperty("reference").GetString()!.Contains("demouser"), "the reference must not carry the email");

        var created = JsonDocument.Parse(Maxio.Requests.Single(r => r.Method == "POST" && r.Path == "/subscriptions.json").Body!)
            .RootElement.GetProperty("subscription");
        Assert.AreEqual("eshop-pro", created.GetProperty("product_handle").GetString());
        Assert.AreEqual("remittance", created.GetProperty("payment_collection_method").GetString());
        StringAssert.StartsWith(created.GetProperty("reference").GetString(), "eshop-sub-");

        var mine = await JsonAsync(await SendAsync(HttpMethod.Get, MySubscriptions));
        Assert.AreEqual("Active", mine.GetProperty("enrollmentStatus").GetString());
        var listed = mine.GetProperty("subscriptions").EnumerateArray().Single();
        Assert.AreEqual(subscription.GetProperty("subscriptionId").GetInt32(), listed.GetProperty("subscriptionId").GetInt32());
        Assert.AreEqual("eshop-pro", listed.GetProperty("planHandle").GetString());
    }

    [TestMethod]
    public async Task SubscribeOnLegacyStatementsSiteBillsByInvoice()
    {
        Maxio.RelationshipInvoicing = false;

        Assert.AreEqual(HttpStatusCode.Created, (await SubscribeAsync("basic-plan")).StatusCode);

        var created = JsonDocument.Parse(Maxio.Requests.Single(r => r.Method == "POST" && r.Path == "/subscriptions.json").Body!);
        Assert.AreEqual("invoice", created.RootElement.GetProperty("subscription").GetProperty("payment_collection_method").GetString());
    }

    [TestMethod]
    public async Task DoubleSubmittedSubscribeCreatesOneCustomerAndOneSubscription()
    {
        Maxio.CreateSubscriptionDelay = TimeSpan.FromSeconds(1.5);

        var responses = await Task.WhenAll(SubscribeAsync("eshop-pro"), SubscribeAsync("eshop-pro"));

        CollectionAssert.AreEquivalent(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, responses.Select(r => r.StatusCode).ToArray());
        Assert.AreEqual(1, Maxio.Count("POST", "/customers.json"));
        Assert.AreEqual(1, Maxio.Count("POST", "/subscriptions.json"));

        var again = await SubscribeAsync("eshop-pro");
        Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);
        Assert.AreEqual("AlreadySubscribed", (await JsonAsync(again)).GetProperty("outcome").GetString());
        Assert.AreEqual(1, Maxio.Count("POST", "/subscriptions.json"));
    }

    [TestMethod]
    public async Task SubscribeToAPlanOutsideTheFamilyIsRejectedBeforeAnyWrite()
    {
        var unknown = await SubscribeAsync("api-call");
        var missing = await SendAsync(HttpMethod.Post, Subscriptions, new { });

        Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.AreEqual("UnknownPlan", (await JsonAsync(unknown)).GetProperty("outcome").GetString());
        Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.IsFalse(Maxio.Requests.Any(r => r.Method == "POST"));
    }

    [TestMethod]
    public async Task SubscribeToAnotherPlanWhileActiveIsAConflict()
    {
        await SubscribeAsync("eshop-pro");

        var response = await SubscribeAsync("basic-plan");

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.AreEqual("SubscribedToDifferentPlan", body.GetProperty("outcome").GetString());
        Assert.AreEqual("eshop-pro", body.GetProperty("subscription").GetProperty("planHandle").GetString());
        Assert.AreEqual(1, Maxio.Count("POST", "/subscriptions.json"));
    }

    [TestMethod]
    public async Task SubscribeAfterLocalStateLossAdoptsTheSubscriptionMaxioHolds()
    {
        await SubscribeAsync("eshop-pro");
        await _factory.ResetEnrollmentsAsync(); // e.g. the in-memory database was recycled

        var mine = await JsonAsync(await SendAsync(HttpMethod.Get, MySubscriptions));
        Assert.AreEqual(1, mine.GetProperty("subscriptions").GetArrayLength());

        var response = await SubscribeAsync("eshop-pro");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("AlreadySubscribed", (await JsonAsync(response)).GetProperty("outcome").GetString());
        Assert.AreEqual(1, Maxio.Count("POST", "/customers.json"));
        Assert.AreEqual(1, Maxio.Count("POST", "/subscriptions.json"));
    }

    [TestMethod]
    public async Task ResubscribeAfterTheSubscriptionEndedCreatesANewOne()
    {
        await SubscribeAsync("eshop-pro");
        Maxio.CancelAllSubscriptions();

        var response = await SubscribeAsync("basic-plan");

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual(2, Maxio.Count("POST", "/subscriptions.json"));
        Assert.AreEqual(1, Maxio.Count("POST", "/customers.json"));
    }

    [TestMethod]
    public async Task CreateSubscriptionConnectionFailureAfterMaxioActedIsSettledByReference()
    {
        Maxio.Fault = r =>
        {
            if (r.Method != "POST" || r.Path != "/subscriptions.json") return null;
            Maxio.Route(r); // Maxio creates it, then the connection drops before the response arrives
            throw new HttpRequestException("connection reset");
        };

        var response = await SubscribeAsync("eshop-pro");

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual("active", (await JsonAsync(response)).GetProperty("subscription").GetProperty("state").GetString());
        Assert.AreEqual(1, Maxio.Count("POST", "/subscriptions.json"), "a POST must never be resent");
        Assert.AreEqual(1, Maxio.Count("GET", "/subscriptions/lookup.json"));
        Assert.AreEqual(1, Maxio.SubscriptionCount);
    }

    [TestMethod]
    public async Task CreateSubscriptionConnectionFailureBeforeMaxioActedReleasesTheClaim()
    {
        Maxio.Fault = r => r.Method == "POST" && r.Path == "/subscriptions.json"
            ? throw new HttpRequestException("connection refused")
            : null;

        var failed = await SubscribeAsync("eshop-pro");

        Assert.AreEqual(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.AreEqual(1, Maxio.Count("GET", "/subscriptions/lookup.json"));
        var mine = await JsonAsync(await SendAsync(HttpMethod.Get, MySubscriptions));
        Assert.AreEqual(JsonValueKind.Null, mine.GetProperty("enrollmentStatus").ValueKind);

        Maxio.Fault = null;
        Assert.AreEqual(HttpStatusCode.Created, (await SubscribeAsync("eshop-pro")).StatusCode);
        Assert.AreEqual(1, Maxio.SubscriptionCount);
    }

    [TestMethod]
    public async Task CreateSubscriptionWithUnconfirmableOutcomeIsAcceptedAndSettledLater()
    {
        Maxio.Fault = r =>
        {
            if (r.Method == "POST" && r.Path == "/subscriptions.json")
            {
                Maxio.Route(r);
                throw new HttpRequestException("connection reset");
            }

            return r.Path == "/subscriptions/lookup.json"
                ? FakeMaxio.Json(HttpStatusCode.BadRequest, new { errors = new[] { "lookup unavailable" } })
                : null;
        };

        var response = await SubscribeAsync("eshop-pro");

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        Assert.AreEqual("OutcomeUnknown", (await JsonAsync(response)).GetProperty("outcome").GetString());

        var retry = await SubscribeAsync("eshop-pro"); // still unconfirmable: must not create a second one
        Assert.AreEqual(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.AreEqual(1, Maxio.Count("POST", "/subscriptions.json"));

        Maxio.Fault = null;
        var mine = await JsonAsync(await SendAsync(HttpMethod.Get, MySubscriptions));
        Assert.AreEqual("Active", mine.GetProperty("enrollmentStatus").GetString());
        Assert.AreEqual(1, mine.GetProperty("subscriptions").GetArrayLength());
        Assert.AreEqual(1, Maxio.SubscriptionCount);
    }

    [TestMethod]
    public async Task CreateCustomerConnectionFailureAfterMaxioActedIsSettledByReference()
    {
        Maxio.Fault = r =>
        {
            if (r.Method != "POST" || r.Path != "/customers.json") return null;
            Maxio.Route(r);
            throw new HttpRequestException("connection reset");
        };

        var response = await SubscribeAsync("eshop-pro");

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual(1, Maxio.Count("POST", "/customers.json"));
        Assert.AreEqual(2, Maxio.Count("GET", "/customers/lookup.json")); // the lookup before the create, then the settle
    }

    [TestMethod]
    public async Task SubscriptionRejectedByMaxioReturnsItsErrorsAndReleasesTheClaim()
    {
        Maxio.Fault = r => r.Method == "POST" && r.Path == "/subscriptions.json"
            ? FakeMaxio.Json(HttpStatusCode.UnprocessableEntity, new { errors = new[] { "No payment method was on file" } })
            : null;

        var rejected = await SubscribeAsync("eshop-pro");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        var body = await JsonAsync(rejected);
        Assert.AreEqual("No payment method was on file", body.GetProperty("Errors")[0].GetString());

        Maxio.Fault = null;
        Assert.AreEqual(HttpStatusCode.Created, (await SubscribeAsync("eshop-pro")).StatusCode);
    }

    [TestMethod]
    public async Task RejectedCredentialsSurfaceAsBadGatewayWithoutProviderDetail()
    {
        Maxio.Fault = _ => FakeMaxio.Json(HttpStatusCode.Unauthorized, new { errors = new[] { "HTTP Basic: Access denied." } });

        var response = await SendAsync(HttpMethod.Get, Plans);

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(text, "The billing system is unavailable.");
        Assert.IsFalse(text.Contains("chargify", StringComparison.OrdinalIgnoreCase), "no provider URL in the response");
    }

    [TestMethod]
    public async Task BaseUrlOverrideIsUsedVerbatim()
    {
        using var factory = new PublicApiTestFactory(new Dictionary<string, string?>
        {
            ["Maxio:BaseUrl"] = "https://billing-proxy.example.test",
            ["Maxio:Subdomain"] = ""
        });
        using var client = factory.CreateClient();

        var response = await SendAsync(HttpMethod.Get, Plans, client: client);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(factory.Maxio.Requests.All(r => r.Host == "billing-proxy.example.test"));
    }

    [TestMethod]
    public void HostRefusesToStartWithoutAnApiKey()
    {
        using var factory = new PublicApiTestFactory(new Dictionary<string, string?> { ["Maxio:ApiKey"] = " " });

        var error = Assert.ThrowsException<Microsoft.Extensions.Options.OptionsValidationException>(() => factory.CreateClient());

        StringAssert.Contains(error.Message, "Maxio:ApiKey is not configured");
    }

    private Task<HttpResponseMessage> SubscribeAsync(string planHandle) =>
        SendAsync(HttpMethod.Post, Subscriptions, new { planHandle });

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null,
        string? token = "", HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token == "" ? ApiTokenHelper.GetNormalUserToken() : token);
        }

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return await (client ?? _client).SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
