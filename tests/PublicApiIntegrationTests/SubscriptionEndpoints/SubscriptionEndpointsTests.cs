using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly FakeMaxio Maxio = new();
    private static WebApplicationFactory<Program> _factory = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        Maxio.Products = new JsonArray(
            FakeMaxio.Product(1, "pro", 29900),
            FakeMaxio.Product(2, "basic", 2900),
            FakeMaxio.Product(3, "card-only", 9900, requireCard: true),
            FakeMaxio.Product(4, "retired", 1000, archived: true));

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(ProgramTest.TestMaxioSettings));
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => Maxio));
        });
    }

    [ClassCleanup]
    public static void ClassCleanup() => _factory.Dispose();

    [TestInitialize]
    public void TestInitialize() => Maxio.Reset();

    // ------------------------------------------------------------------ plans

    [TestMethod]
    public async Task Plans_ReturnsOnlyActivePlansThatNeedNoCard()
    {
        var client = await ClientForNewUserAsync();

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await Read<ListSubscriptionPlansResponse>(response);
        CollectionAssert.AreEquivalent(new[] { "pro", "basic" }, body.Plans.Select(p => p.Handle).ToArray());
        Assert.IsTrue(body.IsComplete);
        var pro = body.Plans.Single(p => p.Handle == "pro");
        Assert.AreEqual(299.00m, pro.Price);
        Assert.AreEqual("month", pro.IntervalUnit);
        StringAssert.Contains(Maxio.Calls.Last(c => c.Path.EndsWith("/products.json")).Path, "handle:test-family");
    }

    [TestMethod]
    public async Task Endpoints_RequireAJwt()
    {
        var anonymous = _factory.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("api/subscriptions", JsonBody(new { planHandle = "pro" }))).StatusCode);
    }

    [TestMethod]
    public async Task Plans_ProviderOutage_Returns503WithRetryAfter()
    {
        var client = await ClientForNewUserAsync();
        Maxio.Intercept = (call, _) => call.Path.EndsWith("/products.json")
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : null;

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.IsTrue(response.Headers.Contains("Retry-After"));
    }

    // ------------------------------------------------------------------ subscribe: hero flow

    [TestMethod]
    public async Task Subscribe_CreatesCustomerAndSubscription_AndConfirmsPlanPriceStateNextBilling()
    {
        var (client, userName) = await ClientAndUserAsync();

        var response = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "pro", firstName = "Ada", lastName = "Lovelace" }));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await Read<CreateSubscriptionResponse>(response);
        Assert.AreEqual("created", body.Status);
        Assert.IsNotNull(body.Subscription);
        Assert.AreEqual("pro", body.Subscription.PlanHandle);
        Assert.AreEqual(29900, body.Subscription.PriceInCents);
        Assert.AreEqual("active", body.Subscription.State);
        Assert.IsNotNull(body.Subscription.NextBillingAt);

        var customer = Maxio.CustomerByReference(userName)!;
        Assert.AreEqual("Ada", (string?)customer["first_name"]);
        Assert.AreEqual(userName, (string?)customer["email"]);

        var sent = Maxio.Calls.Last(c => c.Method == HttpMethod.Post && c.Path == "/subscriptions.json");
        var sentSubscription = JsonNode.Parse(sent.Body!)!["subscription"]!;
        Assert.AreEqual("remittance", (string?)sentSubscription["payment_collection_method"]);
        Assert.AreEqual(body.SubscriptionReference, (string?)sentSubscription["reference"]);

        var mine = await Read<ListMySubscriptionsResponse>(await client.GetAsync("api/my-subscriptions"));
        Assert.AreEqual(1, mine.Subscriptions.Count);
        Assert.AreEqual(body.Subscription.Id, mine.Subscriptions[0].Id);
        Assert.AreEqual(0, mine.Pending.Count);
    }

    [TestMethod]
    public async Task Subscribe_ConcurrentDoubleClick_ReachesMaxioOnce()
    {
        var (client, userName) = await ClientAndUserAsync();
        Maxio.CreateDelay = TimeSpan.FromMilliseconds(400);
        var postsBefore = Maxio.Count(HttpMethod.Post, "/subscriptions.json");
        var customersBefore = Maxio.Count(HttpMethod.Post, "/customers.json");

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "pro" }))));

        Assert.AreEqual(1, Maxio.Count(HttpMethod.Post, "/customers.json") - customersBefore, "exactly one customer create");
        Assert.AreEqual(1, Maxio.Count(HttpMethod.Post, "/subscriptions.json") - postsBefore, "exactly one subscription create");
        Assert.AreEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.IsTrue(responses.All(r => r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict or HttpStatusCode.OK),
            string.Join(",", responses.Select(r => (int)r.StatusCode)));
        var customerId = (int)Maxio.CustomerByReference(userName)!["id"]!;
        Assert.AreEqual(1, Maxio.SubscriptionsFor(customerId).Count);
    }

    [TestMethod]
    public async Task Subscribe_Repeated_ReturnsExistingSubscriptionWithoutCreatingAnother()
    {
        var client = await ClientForNewUserAsync();
        var first = await Read<CreateSubscriptionResponse>(await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "basic" })));
        var postsBefore = Maxio.Count(HttpMethod.Post, "/subscriptions.json");

        var second = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "basic" }));

        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var body = await Read<CreateSubscriptionResponse>(second);
        Assert.AreEqual("already_subscribed", body.Status);
        Assert.AreEqual(first.Subscription!.Id, body.Subscription!.Id);
        Assert.AreEqual(postsBefore, Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task Subscribe_PlanNotOffered_Returns400WithoutWritingToMaxio()
    {
        var client = await ClientForNewUserAsync();
        var writesBefore = Maxio.Calls.Count(c => c.Method == HttpMethod.Post);

        var cardOnly = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "card-only" }));
        var unknown = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "no-such-plan" }));
        var missing = await client.PostAsync("api/subscriptions", JsonBody(new { }));

        Assert.AreEqual(HttpStatusCode.BadRequest, cardOnly.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.AreEqual(writesBefore, Maxio.Calls.Count(c => c.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task Subscribe_MaxioRejects_Returns422AndReleasesTheClaim()
    {
        var client = await ClientForNewUserAsync();
        Maxio.Intercept = (call, _) => call.Method == HttpMethod.Post && call.Path == "/subscriptions.json"
            ? FakeMaxio.Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("No payment method was on file") })
            : null;

        var rejected = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "pro" }));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        StringAssert.Contains(await rejected.Content.ReadAsStringAsync(), "No payment method was on file");

        Maxio.Reset();
        var retry = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "pro" }));
        Assert.AreEqual(HttpStatusCode.Created, retry.StatusCode, "the claim must not block a retry after a rejection");
    }

    // ------------------------------------------------------------------ unknown outcomes

    [TestMethod]
    public async Task Subscribe_ConnectionDropsAfterMaxioCreated_SettlesAsCreated()
    {
        var (client, userName) = await ClientAndUserAsync();
        Maxio.Intercept = (call, fake) =>
        {
            if (call.Method == HttpMethod.Post && call.Path == "/subscriptions.json")
            {
                fake.Route(call); // Maxio acts…
                throw new HttpRequestException("connection reset"); // …but the response never arrives
            }
            return null;
        };

        var response = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "pro" }));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await Read<CreateSubscriptionResponse>(response);
        Assert.AreEqual("created", body.Status);
        Assert.AreEqual(1, Maxio.Calls.Count(c => c.Method == HttpMethod.Post && c.Path == "/subscriptions.json" && FakeMaxio.ReferenceSentIn(c) == body.SubscriptionReference),
            "the write is never resent");
        var customerId = (int)Maxio.CustomerByReference(userName)!["id"]!;
        Assert.AreEqual(1, Maxio.SubscriptionsFor(customerId).Count);
    }

    [TestMethod]
    public async Task Subscribe_ConnectionDropsBeforeMaxioActed_Returns503_AndRetryReusesTheReference()
    {
        var (client, userName) = await ClientAndUserAsync();
        Maxio.Intercept = (call, _) => call.Method == HttpMethod.Post && call.Path == "/subscriptions.json"
            ? throw new HttpRequestException("connection refused")
            : null;

        var failed = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "pro" }));
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        var firstReference = FakeMaxio.ReferenceSentIn(Maxio.Calls.Last(c => c.Method == HttpMethod.Post && c.Path == "/subscriptions.json"));

        Maxio.Reset();
        var retry = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "pro" }));

        Assert.AreEqual(HttpStatusCode.Created, retry.StatusCode);
        var body = await Read<CreateSubscriptionResponse>(retry);
        Assert.AreEqual(firstReference, body.SubscriptionReference);
        var customerId = (int)Maxio.CustomerByReference(userName)!["id"]!;
        Assert.AreEqual(1, Maxio.SubscriptionsFor(customerId).Count);
    }

    [TestMethod]
    public async Task Subscribe_OutcomeCannotBeReadBack_Returns202_AndMySubscriptionsSettlesIt()
    {
        var client = await ClientForNewUserAsync();
        var writeSent = false;
        Maxio.Intercept = (call, fake) =>
        {
            if (call.Method == HttpMethod.Post && call.Path == "/subscriptions.json")
            {
                writeSent = true;
                fake.Route(call);
                throw new HttpRequestException("connection reset");
            }
            // After the write, the re-read fails too (lookup and list are down).
            if (writeSent && call.Method == HttpMethod.Get)
            {
                throw new HttpRequestException("still down");
            }
            return null;
        };

        var response = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "pro" }));

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        var body = await Read<CreateSubscriptionResponse>(response);
        Assert.AreEqual("pending_confirmation", body.Status);
        Assert.IsNull(body.Subscription);

        Maxio.Reset();
        var mine = await Read<ListMySubscriptionsResponse>(await client.GetAsync("api/my-subscriptions"));
        Assert.AreEqual(1, mine.Subscriptions.Count);
        Assert.AreEqual(body.SubscriptionReference, mine.Subscriptions[0].Reference);
        Assert.AreEqual(0, mine.Pending.Count);

        var again = await Read<CreateSubscriptionResponse>(await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "pro" })));
        Assert.AreEqual("already_subscribed", again.Status);
    }

    [TestMethod]
    public async Task Subscribe_ConnectionDropsAfterMaxioCreatedCustomer_SettlesByReference()
    {
        var (client, userName) = await ClientAndUserAsync();
        Maxio.Intercept = (call, fake) =>
        {
            if (call.Method == HttpMethod.Post && call.Path == "/customers.json")
            {
                fake.Route(call);
                throw new HttpRequestException("connection reset");
            }
            return null;
        };

        var response = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "pro" }));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual(1, Maxio.Calls.Count(c => c.Method == HttpMethod.Post && c.Path == "/customers.json" && c.Body!.Contains(userName)));
    }

    [TestMethod]
    public async Task Subscribe_LocalStateLost_DoesNotCreateASecondSubscription()
    {
        var (client, userName) = await ClientAndUserAsync();
        await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "pro" }));

        // Simulate a restart of the in-memory store: drop this user's local rows.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Microsoft.eShopWeb.Infrastructure.Data.CatalogContext>();
            db.SubscriptionClaims.RemoveRange(db.SubscriptionClaims.Where(c => c.CustomerReference == userName));
            db.BillingCustomerLinks.RemoveRange(db.BillingCustomerLinks.Where(l => l.CustomerReference == userName));
            await db.SaveChangesAsync();
        }
        var postsBefore = Maxio.Count(HttpMethod.Post, "/subscriptions.json");

        var response = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "pro" }));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("already_subscribed", (await Read<CreateSubscriptionResponse>(response)).Status);
        Assert.AreEqual(postsBefore, Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    // ------------------------------------------------------------------ configuration

    [TestMethod]
    public void Startup_FailsFast_WhenApiKeyIsMissing()
    {
        var settings = new Dictionary<string, string?>(ProgramTest.TestMaxioSettings) { ["Maxio:ApiKey"] = " " };
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings)));

        var ex = Assert.ThrowsException<OptionsValidationException>(() => factory.CreateClient());
        StringAssert.Contains(ex.Message, "Maxio:ApiKey");
    }

    [TestMethod]
    public async Task BaseUrl_WhenSet_IsUsedVerbatimInsteadOfTheSubdomain()
    {
        var client = await ClientForNewUserAsync();

        await client.GetAsync("api/subscription-plans");

        // Maxio:Subdomain is also set ("test-site"); the override must win.
        Assert.AreEqual("maxio.test.invalid", Maxio.Calls.Last().Host);
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<HttpClient> ClientForNewUserAsync() => (await ClientAndUserAsync()).Client;

    private static async Task<(HttpClient Client, string UserName)> ClientAndUserAsync()
    {
        var userName = $"shopper-{Guid.NewGuid():N}@example.com";
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var result = await users.CreateAsync(new ApplicationUser { UserName = userName, Email = userName }, "Pass@word1");
            Assert.IsTrue(result.Succeeded, string.Join(",", result.Errors.Select(e => e.Description)));
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetTokenFor(userName));
        return (client, userName);
    }

    private static StringContent JsonBody(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(text, Json) ?? throw new AssertFailedException($"Empty body: {text}");
    }
}
