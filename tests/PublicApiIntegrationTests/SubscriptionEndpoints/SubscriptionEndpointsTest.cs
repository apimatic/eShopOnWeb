using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private const string DemoUser = "demouser@microsoft.com";

    [TestMethod]
    public async Task ListsNonArchivedPlansFromConfiguredFamilyUsingBaseUrlOverrideAndBasicAuth()
    {
        using var app = new MaxioTestApplication();
        var client = app.CreateClientFor(ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>()!;
        CollectionAssert.AreEquivalent(new[] { "pro-plan", "basic-plan" }, model.Plans.Select(p => p.Handle).ToArray());
        var pro = model.Plans.Single(p => p.Handle == "pro-plan");
        Assert.AreEqual(29900, pro.PriceInCents);
        Assert.AreEqual(299.00m, pro.Price);
        Assert.AreEqual("month", pro.IntervalUnit);
        Assert.IsFalse(model.IsTruncated);

        var sent = app.Maxio.Requests.Single();
        Assert.AreEqual("maxio.test.invalid", sent.Host, "Maxio:BaseUrl must be used verbatim as the base address");
        Assert.AreEqual("/product_families/handle:test-family/products.json", sent.Path);
        var expectedAuth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("test-api-key-not-a-secret:x"));
        Assert.AreEqual(expectedAuth, sent.Authorization);
    }

    [TestMethod]
    public async Task ReportsTruncationWhenTheCatalogExceedsTheReadCap()
    {
        using var app = new MaxioTestApplication();
        app.Maxio.Products.Clear();
        app.Maxio.Products.AddRange(Enumerable.Range(0, 2001).Select(i => new FakeProduct($"plan-{i}", $"Plan {i}", 100)));
        var client = app.CreateClientFor(ApiTokenHelper.GetNormalUserToken());

        var model = (await (await client.GetAsync("api/subscription-plans")).Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>()!;

        Assert.IsTrue(model.IsTruncated);
        Assert.AreEqual(2000, model.Plans.Count);
    }

    [TestMethod]
    public async Task RequiresABearerToken()
    {
        using var app = new MaxioTestApplication();
        var client = app.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsync("api/subscriptions", Body("pro-plan"))).StatusCode);
        Assert.IsTrue(app.Maxio.Requests.IsEmpty);
    }

    [TestMethod]
    public async Task SubscribeCreatesCustomerAndSubscriptionAndConfirmsPlanPriceStateAndNextBillingDate()
    {
        using var app = new MaxioTestApplication();
        var client = app.CreateClientFor(ApiTokenHelper.GetNormalUserToken());
        var userId = await UserIdAsync(app, DemoUser);

        var response = await client.PostAsync("api/subscriptions", Body("pro-plan", firstName: "Demo", lastName: "User"));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        Assert.IsFalse(model.AlreadyExisted);
        Assert.AreEqual("pro-plan", model.Subscription.PlanHandle);
        Assert.AreEqual("Pro Plan", model.Subscription.PlanName);
        Assert.AreEqual("active", model.Subscription.State);
        Assert.AreEqual(299.00m, model.Subscription.Price);
        Assert.AreEqual("USD", model.Subscription.Currency);
        Assert.IsNotNull(model.Subscription.NextBillingAt);

        var customer = app.Maxio.Customers.Single();
        Assert.AreEqual(userId, customer.Reference, "the Maxio customer reference is the eShop user id");
        Assert.AreEqual(DemoUser, customer.Email);
        Assert.AreEqual("Demo", customer.FirstName);

        var createBody = JsonNode.Parse(app.Maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/subscriptions.json").Body!)!["subscription"]!;
        Assert.AreEqual("pro-plan", createBody["product_handle"]!.GetValue<string>());
        Assert.AreEqual(customer.Id, createBody["customer_id"]!.GetValue<int>());
        Assert.AreEqual(app.Maxio.Subscriptions.Single().Reference, createBody["reference"]!.GetValue<string>());
        Assert.AreEqual("remittance", createBody["payment_collection_method"]!.GetValue<string>(), "no card is captured, so the subscription is invoiced");
    }

    [TestMethod]
    public async Task RepeatingSubscribeReturnsTheExistingSubscriptionWithoutCreatingAnother()
    {
        using var app = new MaxioTestApplication();
        var client = app.CreateClientFor(ApiTokenHelper.GetNormalUserToken());

        var first = await client.PostAsync("api/subscriptions", Body("pro-plan"));
        var second = await client.PostAsync("api/subscriptions", Body("pro-plan"));

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var model = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        Assert.IsTrue(model.AlreadyExisted);
        Assert.AreEqual(1, app.Maxio.CountRequests(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(1, app.Maxio.CountRequests(HttpMethod.Post, "/subscriptions.json"));

        var other = await client.PostAsync("api/subscriptions", Body("basic-plan"));
        Assert.AreEqual(HttpStatusCode.Conflict, other.StatusCode, "a different plan while subscribed is a conflict, not a second subscription");
        Assert.AreEqual(1, app.Maxio.CountRequests(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task ConcurrentDoubleClickReachesMaxioAtMostOnce()
    {
        using var app = new MaxioTestApplication();
        app.Maxio.CreateDelay = TimeSpan.FromMilliseconds(400);
        var client = app.CreateClientFor(ApiTokenHelper.GetNormalUserToken());

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.PostAsync("api/subscriptions", Body("pro-plan"))));

        Assert.AreEqual(1, app.Maxio.CountRequests(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(1, app.Maxio.CountRequests(HttpMethod.Post, "/subscriptions.json"));
        Assert.AreEqual(1, app.Maxio.Customers.Count);
        Assert.AreEqual(1, app.Maxio.Subscriptions.Count);
        Assert.AreEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.IsTrue(responses.All(r => r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Conflict),
            string.Join(",", responses.Select(r => (int)r.StatusCode)));
    }

    [TestMethod]
    public async Task UnknownPlanIsRejectedBeforeAnythingIsWritten()
    {
        using var app = new MaxioTestApplication();
        var client = app.CreateClientFor(ApiTokenHelper.GetNormalUserToken());

        var unknown = await client.PostAsync("api/subscriptions", Body("no-such-plan"));
        var archived = await client.PostAsync("api/subscriptions", Body("old-plan"));

        Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, archived.StatusCode);
        Assert.IsFalse(app.Maxio.Requests.Any(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task LostResponseAfterCreateIsSettledByReferenceAndNotCreatedTwice()
    {
        using var app = new MaxioTestApplication();
        app.Maxio.DropResponseAfterCreatingSubscription = true;
        var client = app.CreateClientFor(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", Body("pro-plan"));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        Assert.AreEqual(app.Maxio.Subscriptions.Single().Id, model.Subscription.Id);
        Assert.AreEqual(1, app.Maxio.CountRequests(HttpMethod.Post, "/subscriptions.json"), "the SDK must not resend the POST");
        Assert.AreEqual(1, app.Maxio.CountRequests(HttpMethod.Get, "/subscriptions/lookup.json"));

        app.Maxio.DropResponseAfterCreatingSubscription = false;
        var again = await client.PostAsync("api/subscriptions", Body("pro-plan"));
        Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);
        Assert.AreEqual(1, app.Maxio.Subscriptions.Count);
    }

    [TestMethod]
    public async Task LostResponseAfterCustomerCreateIsSettledByReferenceAndTheSubscriptionProceeds()
    {
        using var app = new MaxioTestApplication();
        app.Maxio.DropResponseAfterCreatingCustomer = true;
        var client = app.CreateClientFor(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", Body("pro-plan"));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(1, app.Maxio.Customers.Count);
        Assert.AreEqual(1, app.Maxio.CountRequests(HttpMethod.Post, "/customers.json"), "the SDK must not resend the POST");
        Assert.AreEqual(app.Maxio.Customers.Single().Id, app.Maxio.Subscriptions.Single().Customer.Id);
    }

    [TestMethod]
    public async Task UnsettledCreateIsReportedPendingAndBlocksASecondCreate()
    {
        using var app = new MaxioTestApplication();
        app.Maxio.DropConnectionBeforeCreatingSubscription = true;
        var client = app.CreateClientFor(ApiTokenHelper.GetNormalUserToken());

        var first = await client.PostAsync("api/subscriptions", Body("pro-plan"));
        Assert.AreEqual(HttpStatusCode.Accepted, first.StatusCode, "an unconfirmed write is pending, not failed");

        app.Maxio.DropConnectionBeforeCreatingSubscription = false;
        var second = await client.PostAsync("api/subscriptions", Body("pro-plan"));
        Assert.AreEqual(HttpStatusCode.Conflict, second.StatusCode, "the pending claim holds until it settles or its lease expires");
        Assert.IsTrue(second.Headers.Contains("Retry-After"));
        Assert.AreEqual(1, app.Maxio.CountRequests(HttpMethod.Post, "/subscriptions.json"));
        Assert.AreEqual(0, app.Maxio.Subscriptions.Count);
    }

    [TestMethod]
    public async Task StalePendingClaimIsReleasedAfterItsLeaseWhenMaxioHasNoSuchSubscription()
    {
        using var app = new MaxioTestApplication();
        app.Maxio.DropConnectionBeforeCreatingSubscription = true;
        var client = app.CreateClientFor(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.Accepted, (await client.PostAsync("api/subscriptions", Body("pro-plan"))).StatusCode);

        app.Maxio.DropConnectionBeforeCreatingSubscription = false;
        app.Clock.Advance(TimeSpan.FromMinutes(3));
        var retried = await client.PostAsync("api/subscriptions", Body("pro-plan"));

        Assert.AreEqual(HttpStatusCode.Created, retried.StatusCode);
        Assert.AreEqual(1, app.Maxio.Subscriptions.Count);
    }

    [TestMethod]
    public async Task ProviderRejectionIsSurfacedAndReleasesTheClaim()
    {
        using var app = new MaxioTestApplication();
        app.Maxio.RejectSubscriptionErrors = new[] { "A payment profile is required." };
        var client = app.CreateClientFor(ApiTokenHelper.GetNormalUserToken());

        var rejected = await client.PostAsync("api/subscriptions", Body("pro-plan"));
        Assert.AreEqual((HttpStatusCode)422, rejected.StatusCode);
        var error = (await rejected.Content.ReadAsStringAsync()).FromJson<SubscriptionErrorResponse>()!;
        CollectionAssert.Contains(error.Errors, "A payment profile is required.");

        app.Maxio.RejectSubscriptionErrors = null;
        var retried = await client.PostAsync("api/subscriptions", Body("pro-plan"));
        Assert.AreEqual(HttpStatusCode.Created, retried.StatusCode);
        Assert.AreEqual(1, app.Maxio.Customers.Count, "the customer is reused, not created again");
    }

    [TestMethod]
    public async Task AdoptsALiveSubscriptionMaxioAlreadyHoldsForTheCustomer()
    {
        using var app = new MaxioTestApplication();
        var client = app.CreateClientFor(ApiTokenHelper.GetNormalUserToken());
        var customer = app.Maxio.AddCustomer(await UserIdAsync(app, DemoUser), DemoUser);
        var existing = app.Maxio.AddSubscription(customer, "basic-plan");

        var response = await client.PostAsync("api/subscriptions", Body("basic-plan"));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        Assert.IsTrue(model.AlreadyExisted);
        Assert.AreEqual(existing.Id, model.Subscription.Id);
        Assert.IsFalse(app.Maxio.Requests.Any(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task MySubscriptionsReflectsMaxio()
    {
        using var app = new MaxioTestApplication();
        var client = app.CreateClientFor(ApiTokenHelper.GetNormalUserToken());

        var before = (await (await client.GetAsync("api/my-subscriptions")).Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>()!;
        Assert.AreEqual(0, before.Subscriptions.Count);
        Assert.IsFalse(app.Maxio.Requests.Any(r => r.Method == HttpMethod.Post), "reading never creates a customer");

        await client.PostAsync("api/subscriptions", Body("basic-plan"));
        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>()!;
        var subscription = model.Subscriptions.Single();
        Assert.AreEqual("basic-plan", subscription.PlanHandle);
        Assert.AreEqual(29.00m, subscription.Price);
        Assert.AreEqual("active", subscription.State);
        Assert.IsTrue(subscription.IsLive);
        Assert.IsNotNull(subscription.NextBillingAt);
    }

    [TestMethod]
    public async Task RejectedCredentialsBecomeACallerSafe503()
    {
        using var app = new MaxioTestApplication();
        app.Maxio.ForceStatus = HttpStatusCode.Unauthorized;
        var client = app.CreateClientFor(ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode, "our credentials failing is not the caller's 401");
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.DoesNotMatch(body, new System.Text.RegularExpressions.Regex("maxio\\.test\\.invalid|Exception|MaxioAdvancedBilling"));
    }

    [TestMethod]
    public void HostRefusesToStartWithoutAnApiKey()
    {
        using var app = new MaxioTestApplication(o => o.ApiKey = " ");

        var ex = Assert.ThrowsException<OptionsValidationException>(() => app.CreateClient());

        StringAssert.Contains(ex.Message, "Maxio:ApiKey");
        StringAssert.DoesNotMatch(ex.Message, new System.Text.RegularExpressions.Regex("test-api-key"));
    }

    private static StringContent Body(string planHandle, string? firstName = null, string? lastName = null) =>
        new(JsonSerializer.Serialize(new { planHandle, firstName, lastName }), Encoding.UTF8, "application/json");

    private static async Task<string> UserIdAsync(MaxioTestApplication app, string userName)
    {
        using var scope = app.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync(userName);
        return user!.Id;
    }
}
