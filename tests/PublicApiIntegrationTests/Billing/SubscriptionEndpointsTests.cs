using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Billing;

/// <summary>
/// Drives the three subscription endpoints through the real PublicApi host (JWT auth, routing, DI, EF in-memory),
/// with the Maxio SDK client re-pointed at <see cref="FakeMaxio"/>. No traffic reaches Maxio.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTests
{
    private static FakeMaxio _maxio = null!;
    private static WebApplicationFactory<Program> _factory = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _maxio = FakeMaxio.WithDefaultPlans();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<MaxioSettings>(s => s.ProductFamilyHandle = FakeMaxio.FamilyHandle);
            services.RemoveAll<MaxioAdvancedBillingClient>();
            services.AddSingleton(_ => _maxio.CreateClient());
        }));
    }

    [ClassCleanup]
    public static void ClassCleanup() => _factory.Dispose();

    private static HttpClient ClientFor(string? userName)
    {
        var client = _factory.CreateClient();
        if (userName is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(userName));
        }

        return client;
    }

    private static string NewUser() => $"subscriber-{Guid.NewGuid():N}@example.test";

    [TestMethod]
    public async Task AllSubscriptionEndpoints_RequireAJwt()
    {
        var client = ClientFor(null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "pro-test" })).StatusCode);
    }

    [TestMethod]
    public async Task ListPlans_ReturnsTheOfferedPlans()
    {
        var response = await ClientFor(NewUser()).GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.AreEqual(false, (bool?)body["isPartial"]);
        var plans = body["plans"]!.AsArray();
        Assert.AreEqual(2, plans.Count);
        Assert.AreEqual("pro-test", (string?)plans[0]!["handle"]);
        Assert.AreEqual(299.00m, (decimal?)plans[0]!["price"]!["amount"]);
        Assert.AreEqual("month", (string?)plans[0]!["interval"]!["unit"]);
    }

    [TestMethod]
    public async Task Subscribe_Returns201_ThenTheRepeatReturns200WithTheSameSubscription_AndMySubscriptionsShowsIt()
    {
        var client = ClientFor(NewUser());

        var created = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "pro-test" });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var createdBody = JsonNode.Parse(await created.Content.ReadAsStringAsync())!;
        Assert.AreEqual("created", (string?)createdBody["outcome"]);
        var subscription = createdBody["subscription"]!;
        var subscriptionId = (int?)subscription["subscriptionId"];
        Assert.IsNotNull(subscriptionId);
        Assert.AreEqual("pro-test", (string?)subscription["planHandle"]);
        Assert.AreEqual("Pro (test)", (string?)subscription["planName"]);
        Assert.AreEqual(299.00m, (decimal?)subscription["price"]!["amount"]);
        Assert.AreEqual(29900L, (long?)subscription["price"]!["amountInCents"]);
        Assert.AreEqual("USD", (string?)subscription["price"]!["currency"]);
        Assert.AreEqual("active", (string?)subscription["state"]);
        Assert.IsNotNull((string?)subscription["nextBillingAt"]);
        Assert.IsNotNull((string?)subscription["createdAt"]);

        var repeat = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "pro-test" });
        Assert.AreEqual(HttpStatusCode.OK, repeat.StatusCode);
        var repeatBody = JsonNode.Parse(await repeat.Content.ReadAsStringAsync())!;
        Assert.AreEqual("existing", (string?)repeatBody["outcome"]);
        Assert.AreEqual(subscriptionId, (int?)repeatBody["subscription"]!["subscriptionId"]);

        var mine = await client.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.OK, mine.StatusCode);
        var list = JsonNode.Parse(await mine.Content.ReadAsStringAsync())!["subscriptions"]!.AsArray();
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(subscriptionId, (int?)list[0]!["subscriptionId"]);
    }

    [TestMethod]
    public async Task Subscribe_UnknownPlan_Returns400ProblemDetails()
    {
        var response = await ClientFor(NewUser()).PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.AreEqual(400, (int?)problem["status"]);
        Assert.IsNotNull((string?)problem["correlationId"]);
    }

    [TestMethod]
    public async Task MySubscriptions_ForAShopperWithNoBillingAccount_IsEmpty()
    {
        var response = await ClientFor(NewUser()).GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(0, JsonNode.Parse(await response.Content.ReadAsStringAsync())!["subscriptions"]!.AsArray().Count);
    }

    [TestMethod]
    public async Task Swagger_DocumentsTheSubscriptionEndpoints()
    {
        var response = await ClientFor(null).GetAsync("swagger/v1/swagger.json");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var paths = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["paths"]!.AsObject();
        Assert.IsTrue(paths.ContainsKey("/api/subscription-plans"));
        Assert.IsTrue(paths.ContainsKey("/api/subscriptions"));
        Assert.IsTrue(paths.ContainsKey("/api/my-subscriptions"));
    }

    private static string CreateToken(string userName)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, userName) };
        var key = Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
