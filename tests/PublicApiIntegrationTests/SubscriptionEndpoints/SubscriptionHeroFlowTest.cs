using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.PublicApi.AuthEndpoints;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
[DoNotParallelize]
public class SubscriptionHeroFlowTest
{
    [TestMethod]
    public async Task EndpointsRequireBearerAuthentication()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task LoggedInShopperCanListSubscribeIdempotentlyAndReadAccount()
    {
        ProgramTest.BillingGateway.Reset();
        using var client = ProgramTest.NewClient;
        var authResponse = await client.PostAsJsonAsync("api/authenticate", new AuthenticateRequest
        {
            Username = "demouser@microsoft.com",
            Password = AuthorizationConstants.DEFAULT_PASSWORD
        });
        authResponse.EnsureSuccessStatusCode();
        var auth = await authResponse.Content.ReadFromJsonAsync<AuthenticateResponse>();
        Assert.IsNotNull(auth);
        Assert.IsTrue(auth.Result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(auth.Token));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var plans = await client.GetFromJsonAsync<List<SubscriptionPlanDto>>("api/subscription-plans");
        Assert.IsNotNull(plans);
        Assert.AreEqual(2, plans.Count);
        Assert.AreEqual(299m, plans.Single(x => x.Handle == "eshop-pro").Price);

        var request = new CreateSubscriptionRequest { ProductHandle = "eshop-pro" };
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("api/subscriptions", request),
            client.PostAsJsonAsync("api/subscriptions", request));

        foreach (var response in responses)
        {
            response.EnsureSuccessStatusCode();
            var subscription = await response.Content.ReadFromJsonAsync<SubscriptionDto>();
            Assert.IsNotNull(subscription);
            Assert.AreEqual("eshop-pro", subscription.ProductHandle);
            Assert.AreEqual(299m, subscription.Price);
            Assert.AreEqual("active", subscription.State);
            Assert.IsNotNull(subscription.NextBillingDate);
        }

        Assert.AreEqual(1, ProgramTest.BillingGateway.CreateSubscriptionCalls);

        var mySubscriptions = await client.GetFromJsonAsync<List<SubscriptionDto>>("api/my-subscriptions");
        Assert.IsNotNull(mySubscriptions);
        Assert.AreEqual(1, mySubscriptions.Count);
        Assert.AreEqual("eshop-pro", mySubscriptions[0].ProductHandle);
    }
}
