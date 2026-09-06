using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Covers what the subscription endpoints must do without reaching Maxio: refuse anonymous
/// callers, and degrade to a 503 that names the missing configuration rather than a 500.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static WebApplicationFactory<Program> _unconfigured = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        // Pin the section to empty so the outcome does not depend on whatever Maxio credentials
        // happen to be present on the machine running the tests.
        _unconfigured = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Maxio:ApiKey", string.Empty);
            builder.UseSetting("Maxio:Subdomain", string.Empty);
            builder.UseSetting("Maxio:BaseUrl", string.Empty);
            builder.UseSetting("Maxio:ProductFamilyHandle", string.Empty);
        });
    }

    [ClassCleanup]
    public static void ClassCleanup() => _unconfigured?.Dispose();

    [TestMethod]
    public async Task ListPlansRequiresAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsRequiresAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresAToken()
    {
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", ProPlanBody());

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsARequestWithNoPlanHandle()
    {
        var client = AuthenticatedClient();

        var response = await client.PostAsync(
            "api/subscriptions", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "planHandle is required");
    }

    [TestMethod]
    public async Task ListPlansReportsTheMissingConfigurationInsteadOfFailing()
    {
        var client = AuthenticatedClient();

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Maxio:ApiKey");
    }

    [TestMethod]
    public async Task SubscribeReportsTheMissingConfigurationInsteadOfFailing()
    {
        var client = AuthenticatedClient();

        var response = await client.PostAsync("api/subscriptions", ProPlanBody());

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Maxio:ProductFamilyHandle");
    }

    [TestMethod]
    public async Task ListMySubscriptionsReportsTheMissingConfigurationInsteadOfFailing()
    {
        var client = AuthenticatedClient();

        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static HttpClient AuthenticatedClient()
    {
        var client = _unconfigured.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        return client;
    }

    private static StringContent ProPlanBody() =>
        new("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");
}
