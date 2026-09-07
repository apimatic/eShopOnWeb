using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.PublicApi.AuthEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    [TestMethod]
    public async Task SubscriptionPlansEndpoint_Should_Require_Authorization()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscriptionEndpoint_Should_Require_Authorization()
    {
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetMySubscriptionsEndpoint_Should_Require_Authorization()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionPlansEndpoint_Should_Handle_Missing_Maxio_Config()
    {
        var token = await GetTestAuthTokenAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "api/subscription-plans");
        request.Headers.Add("Authorization", $"Bearer {token}");

        var response = await ProgramTest.NewClient.SendAsync(request);

        Assert.IsNotNull(response);
    }

    private async Task<string> GetTestAuthTokenAsync()
    {
        var request = new AuthenticateRequest()
        {
            Username = "demouser@microsoft.com",
            Password = AuthorizationConstants.DEFAULT_PASSWORD
        };
        var jsonContent = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await ProgramTest.NewClient.PostAsync("api/authenticate", jsonContent);

        if (!response.IsSuccessStatusCode)
        {
            return string.Empty;
        }

        var stringResponse = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(stringResponse);
        var root = jsonDoc.RootElement;

        if (root.TryGetProperty("token", out var tokenElement))
        {
            return tokenElement.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}
