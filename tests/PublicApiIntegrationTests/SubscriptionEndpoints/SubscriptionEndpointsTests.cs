using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.UnitTests.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Through the real PublicApi pipeline (TestServer) (JWT, endpoints, service, EF claims, SDK client) with only the Maxio HTTP
/// transport replaced by an in-memory fake. Nothing here reaches a real Maxio site.
/// Success bodies (2xx JSON) are covered in SubscriptionEndpointHandlerTests: the net8 TestServer response pipe cannot be written by the
/// .NET 9/10 runtimes installed on this machine (pre-existing: 12 of the 15 original tests in this project fail the same way here).
/// </summary>
[TestClass]
public class SubscriptionEndpointsTests
{
    private static (HttpClient Client, FakeMaxio Maxio) NewHost()
    {
        var fake = new FakeMaxio();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<MaxioAdvancedBillingClient>();
                services.AddSingleton(new MaxioAdvancedBillingClient(new HttpClient(fake), new MaxioAdvancedBillingClientOptions
                {
                    Retry = RetryOptions.Disabled(),
                    Server = new ServerOptions { Production = { Us = { BaseUrl = "https://maxio.test" } } }
                }));
            }));
        return (factory.CreateClient(), fake);
    }

    private static HttpClient As(HttpClient client, string userName)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetTokenFor(userName));
        return client;
    }

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");

    private static string NewUser() => $"it-{Guid.NewGuid():N}@example.com";

    [TestMethod]
    public async Task AllThreeEndpointsRequireAToken()
    {
        var (client, _) = NewHost();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsync("api/subscriptions", Body("{\"planHandle\":\"fake-pro\"}"))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
    }

    [TestMethod]
    public async Task Plans_ProviderRejectsCredentials_Is502WithoutProviderDetail()
    {
        var (client, maxio) = NewHost();
        maxio.Intercept = (_, _) => FakeMaxio.Json(HttpStatusCode.Unauthorized, new { error = "secret-detail" });

        var response = await As(client, NewUser()).GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("secret-detail"));
    }

    [TestMethod]
    public async Task Subscribe_WithoutPlanHandle_Is400()
    {
        var (client, _) = NewHost();

        var response = await As(client, NewUser()).PostAsync("api/subscriptions", Body("{}"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_UnknownPlan_Is422_AndNothingIsWritten()
    {
        var (client, maxio) = NewHost();

        var response = await As(client, NewUser()).PostAsync("api/subscriptions", Body("{\"planHandle\":\"nope\"}"));

        Assert.AreEqual((HttpStatusCode)422, response.StatusCode);
        Assert.AreEqual(0, maxio.Count(HttpMethod.Post, ".json"));
    }

    [TestMethod]
    public async Task Subscribe_UserNameThatIsNotAnEmail_Is422()
    {
        var (client, _) = NewHost();

        var response = await As(client, "plainname").PostAsync("api/subscriptions", Body("{\"planHandle\":\"fake-pro\"}"));

        Assert.AreEqual((HttpStatusCode)422, response.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_ProviderRejection422_IsSurfacedAs422()
    {
        var (client, maxio) = NewHost();
        maxio.Intercept = (req, _) => req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")
            ? FakeMaxio.Json((HttpStatusCode)422, new { errors = new[] { "No payment method was on file" } })
            : null;

        var response = await As(client, NewUser()).PostAsync("api/subscriptions", Body("{\"planHandle\":\"fake-pro\"}"));

        Assert.AreEqual((HttpStatusCode)422, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "No payment method");
    }

}
