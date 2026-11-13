using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private FakeMaxioHandler _maxio = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [TestInitialize]
    public void Setup()
    {
        _maxio = new FakeMaxioHandler();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => _maxio)));
    }

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    // Every test uses its own shopper, so tests never share claims or Maxio references.
    private HttpClient ClientFor(out string userName)
    {
        userName = $"shopper-{Guid.NewGuid():N}@example.com";
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetUserToken(userName));
        return client;
    }

    private static Task<HttpResponseMessage> Subscribe(HttpClient client, string planHandle) =>
        client.PostAsJsonAsync("api/subscriptions", new { planHandle });

    [TestMethod]
    public async Task EndpointsRequireAJwt()
    {
        var anonymous = _factory.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Subscribe(anonymous, "eshop-pro")).StatusCode);
        Assert.AreEqual(0, _maxio.Requests.Count);
    }

    [TestMethod]
    public async Task ListsThePlansOfTheConfiguredProductFamily()
    {
        var client = ClientFor(out _);

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ListSubscriptionPlansResponse>(Json);
        Assert.IsFalse(body!.Truncated);
        CollectionAssert.AreEqual(new[] { "eshop-pro", "basic-plan" }, body.Plans.Select(p => p.Handle).ToArray());
        Assert.AreEqual(29900, body.Plans[0].PriceInCents);
        Assert.AreEqual("month", body.Plans[0].IntervalUnit);
        // The family comes from configuration (Maxio:ProductFamilyHandle), addressed by handle.
        StringAssert.Contains(_maxio.Requests.Single().Path, "test-family");
    }

    [TestMethod]
    public async Task SubscribeCreatesCustomerAndSubscriptionAndReportsThem()
    {
        var client = ClientFor(out _);

        var response = await Subscribe(client, "eshop-pro");

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(Json);
        Assert.AreEqual("created", body!.Status);
        Assert.AreEqual("eshop-pro", body.Subscription!.PlanHandle);
        Assert.AreEqual("active", body.Subscription.State);
        Assert.AreEqual(29900, body.Subscription.PriceInCents);
        Assert.AreEqual(new DateTimeOffset(2026, 12, 13, 0, 0, 0, TimeSpan.FromHours(-5)), body.Subscription.NextBillingAt);

        // What went over the wire to Maxio.
        var subscriptionBody = JsonDocument.Parse(_maxio.LastBody(HttpMethod.Post, "/subscriptions.json")!).RootElement.GetProperty("subscription");
        Assert.AreEqual("eshop-pro", subscriptionBody.GetProperty("product_handle").GetString());
        Assert.AreEqual(501, subscriptionBody.GetProperty("customer_id").GetInt32());
        StringAssert.StartsWith(subscriptionBody.GetProperty("reference").GetString(), "eshop-sub-");
        Assert.AreEqual("remittance", subscriptionBody.GetProperty("payment_collection_method").GetString());
        Assert.IsFalse(subscriptionBody.TryGetProperty("ref", out _), "'ref' is Maxio's referral code and must never be sent");
        Assert.IsFalse(subscriptionBody.TryGetProperty("customer_attributes", out _), "customer_attributes would create a second customer");

        var customerBody = JsonDocument.Parse(_maxio.LastBody(HttpMethod.Post, "/customers.json")!).RootElement.GetProperty("customer");
        StringAssert.StartsWith(customerBody.GetProperty("reference").GetString(), "eshop-cus-");

        // ...and the shopper's account reflects it.
        var mine = await client.GetFromJsonAsync<ListMySubscriptionsResponse>("api/my-subscriptions", Json);
        Assert.AreEqual(1, mine!.Subscriptions.Count);
        Assert.AreEqual(body.Subscription.Id, mine.Subscriptions[0].Id);
    }

    [TestMethod]
    public async Task DoubleClickSendsExactlyOneCreateToMaxio()
    {
        _maxio.LookupDelay = TimeSpan.FromMilliseconds(300);
        var client = ClientFor(out _);

        var responses = await Task.WhenAll(Subscribe(client, "eshop-pro"), Subscribe(client, "eshop-pro"));

        CollectionAssert.AreEquivalent(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, responses.Select(r => r.StatusCode).ToArray());
        Assert.AreEqual(1, _maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(1, _maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task RepeatingTheSubscribeIsIdempotent()
    {
        var client = ClientFor(out _);
        Assert.AreEqual(HttpStatusCode.Created, (await Subscribe(client, "eshop-pro")).StatusCode);

        var again = await Subscribe(client, "eshop-pro");
        var otherPlan = await Subscribe(client, "basic-plan");

        Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);
        Assert.AreEqual("already_subscribed", (await again.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(Json))!.Status);
        Assert.AreEqual(HttpStatusCode.Conflict, otherPlan.StatusCode);
        Assert.AreEqual(1, _maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.AreEqual(1, _maxio.Count(HttpMethod.Post, "/customers.json"));
    }

    [TestMethod]
    public async Task UnknownPlanIsRejectedBeforeAnythingIsWritten()
    {
        var client = ClientFor(out _);

        var response = await Subscribe(client, "not-a-plan");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, _maxio.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task ProviderRejectionIsSurfacedAndReleasesTheClaim()
    {
        _maxio.CreateSubscription = FakeMaxioHandler.CreateBehavior.Reject;
        var client = ClientFor(out _);

        var rejected = await Subscribe(client, "eshop-pro");

        Assert.AreEqual((HttpStatusCode)422, rejected.StatusCode);
        var error = await rejected.Content.ReadFromJsonAsync<BillingErrorResponse>(Json);
        CollectionAssert.Contains(error!.Errors.ToList(), "No payment method was on file for the $299.00 balance");

        // The refusal released the claim: once the cause is fixed the shopper can subscribe.
        _maxio.CreateSubscription = FakeMaxioHandler.CreateBehavior.Succeed;
        Assert.AreEqual(HttpStatusCode.Created, (await Subscribe(client, "eshop-pro")).StatusCode);
        Assert.AreEqual(1, _maxio.Count(HttpMethod.Post, "/customers.json"), "the customer is reused, not re-created");
    }

    [TestMethod]
    public async Task ConnectionLostAfterTheCreateLandedIsSettledAsCreated()
    {
        _maxio.CreateSubscription = FakeMaxioHandler.CreateBehavior.LandThenDropConnection;
        var client = ClientFor(out _);

        var response = await Subscribe(client, "eshop-pro");

        // The failing write's own catch re-read Maxio by reference and found the subscription.
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual(901, (await response.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(Json))!.Subscription!.Id);
        Assert.AreEqual(1, _maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task ConnectionLostWithoutConfirmationIsPendingAndNeverDuplicated()
    {
        _maxio.CreateSubscription = FakeMaxioHandler.CreateBehavior.DropConnectionWithoutLanding;
        var client = ClientFor(out _);

        var first = await Subscribe(client, "eshop-pro");
        Assert.AreEqual(HttpStatusCode.Accepted, first.StatusCode);
        Assert.AreEqual("pending_confirmation", (await first.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(Json))!.Status);

        // Inside the window in which Maxio could still complete the request, a retry must not create again.
        _maxio.CreateSubscription = FakeMaxioHandler.CreateBehavior.Succeed;
        var retry = await Subscribe(client, "eshop-pro");

        Assert.AreEqual(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.AreEqual(1, _maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task CustomerCreateConnectionLostAfterItLandedIsSettledAndTheSubscribeCompletes()
    {
        _maxio.CreateCustomer = FakeMaxioHandler.CreateBehavior.LandThenDropConnection;
        var client = ClientFor(out _);

        var response = await Subscribe(client, "eshop-pro");

        // The customer create's own catch re-read Maxio by reference, found the customer, and the flow went on.
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual(1, _maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(1, _maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task CustomerCreateConnectionLostWithoutConfirmationSubscribesNothingAndIsNotRepeated()
    {
        _maxio.CreateCustomer = FakeMaxioHandler.CreateBehavior.DropConnectionWithoutLanding;
        var client = ClientFor(out _);

        var first = await Subscribe(client, "eshop-pro");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.AreEqual(0, _maxio.Count(HttpMethod.Post, "/subscriptions.json"), "no subscription without a confirmed customer");

        // The customer claim is Unknown: inside Maxio's completion window a retry must not create a second customer.
        _maxio.CreateCustomer = FakeMaxioHandler.CreateBehavior.Succeed;
        var retry = await Subscribe(client, "eshop-pro");

        Assert.AreEqual(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.AreEqual(1, _maxio.Count(HttpMethod.Post, "/customers.json"));
    }

    [TestMethod]
    public async Task MySubscriptionsIsEmptyForAShopperWithoutABillingAccount()
    {
        var client = ClientFor(out _);

        var mine = await client.GetFromJsonAsync<ListMySubscriptionsResponse>("api/my-subscriptions", Json);

        Assert.AreEqual(0, mine!.Subscriptions.Count);
        Assert.AreEqual(0, _maxio.Requests.Count(r => r.Method == HttpMethod.Post));
    }
}
