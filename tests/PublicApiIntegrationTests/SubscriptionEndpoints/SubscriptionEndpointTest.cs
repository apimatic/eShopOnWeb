using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.PublicApi.AuthEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [TestMethod]
    public async Task ListPlansReturnsUnauthorizedWithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeFlowIsIdempotentAgainstMaxio()
    {
        var client = ProgramTest.NewClient;
        var token = await AuthenticateAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var plansResponse = await client.GetAsync("api/subscription-plans");
        var plansBody = await plansResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, plansResponse.StatusCode, plansBody);

        var plans = JsonSerializer.Deserialize<ListSubscriptionPlansResponse>(plansBody, JsonOptions);
        Assert.IsNotNull(plans);
        Assert.IsTrue(plans.Plans.Count >= 1, "Expected at least one Maxio plan in the configured family.");

        var productHandle = plans.Plans.Exists(p => p.Handle == "eshop-pro")
            ? "eshop-pro"
            : plans.Plans[0].Handle;

        var createContent = new StringContent(
            JsonSerializer.Serialize(new CreateSubscriptionRequest { ProductHandle = productHandle }),
            Encoding.UTF8,
            "application/json");

        var first = await client.PostAsync("api/subscriptions", createContent);
        var firstBody = await first.Content.ReadAsStringAsync();
        Assert.IsTrue(first.IsSuccessStatusCode, firstBody);
        var firstModel = JsonSerializer.Deserialize<CreateSubscriptionResponse>(firstBody, JsonOptions);
        Assert.IsNotNull(firstModel?.Subscription);
        Assert.AreEqual(productHandle, firstModel.Subscription.ProductHandle);
        Assert.IsFalse(string.IsNullOrWhiteSpace(firstModel.Subscription.State));
        Assert.IsTrue(firstModel.Subscription.Price > 0);
        Assert.IsTrue(firstModel.Subscription.Id > 0);

        var secondContent = new StringContent(
            JsonSerializer.Serialize(new CreateSubscriptionRequest { ProductHandle = productHandle }),
            Encoding.UTF8,
            "application/json");
        var second = await client.PostAsync("api/subscriptions", secondContent);
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.IsTrue(second.IsSuccessStatusCode, secondBody);
        var secondModel = JsonSerializer.Deserialize<CreateSubscriptionResponse>(secondBody, JsonOptions);
        Assert.IsNotNull(secondModel?.Subscription);
        Assert.AreEqual(firstModel.Subscription.Id, secondModel.Subscription.Id);
        Assert.IsFalse(secondModel.Created);

        var mine = await client.GetAsync("api/my-subscriptions");
        var mineBody = await mine.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, mine.StatusCode, mineBody);
        var mineModel = JsonSerializer.Deserialize<ListMySubscriptionsResponse>(mineBody, JsonOptions);
        Assert.IsNotNull(mineModel);
        Assert.IsTrue(mineModel.Subscriptions.Exists(s => s.Id == firstModel.Subscription.Id));
    }

    private static async Task<string> AuthenticateAsync(HttpClient client)
    {
        var request = new AuthenticateRequest
        {
            Username = "demouser@microsoft.com",
            Password = AuthorizationConstants.DEFAULT_PASSWORD
        };
        var jsonContent = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/authenticate", jsonContent);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var model = JsonSerializer.Deserialize<AuthenticateResponse>(body, JsonOptions);
        Assert.IsNotNull(model);
        Assert.IsTrue(model.Result, "Expected demo user authentication to succeed.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(model.Token));
        return model.Token;
    }
}
