using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class CreateSubscriptionEndpointTest
{
    [TestMethod]
    public async Task CreatesCustomerAndSubscriptionForNewSubscriber()
    {
        var handler = MaxioTestServer.ForSubscribeFlow();
        using var factory = MaxioTestServer.CreateFactory(handler);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", SubscribeContent());

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(model?.Subscription);
        Assert.IsFalse(model.AlreadyExisted);
        Assert.AreEqual(9001, model.Subscription.Id);
        Assert.AreEqual("active", model.Subscription.State);
        Assert.AreEqual("eshop-pro", model.Subscription.ProductHandle);
        Assert.AreEqual("Pro Plan", model.Subscription.ProductName);
        Assert.AreEqual(299.00m, model.Subscription.Price);
        Assert.IsNotNull(model.Subscription.NextBillingDate);

        // Exactly one customer create and one subscription create reached the provider.
        Assert.AreEqual(1, handler.CountRequests(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(1, handler.CountRequests(HttpMethod.Post, "/subscriptions.json"));

        var createSubscription = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Uri!.AbsolutePath == "/subscriptions.json");
        StringAssert.Contains(createSubscription.Body, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(createSubscription.Body, $"\"customer_id\":{MaxioTestServer.CustomerId}");
        StringAssert.Contains(createSubscription.Body, "\"reference\":\"demouser@microsoft.com:eshop-pro\"");
        // Card-free signup: billed by invoice (remittance), so no payment method is attempted.
        StringAssert.Contains(createSubscription.Body, "\"payment_collection_method\":\"remittance\"");

        var createCustomer = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Uri!.AbsolutePath == "/customers.json");
        StringAssert.Contains(createCustomer.Body, "\"reference\":\"demouser@microsoft.com\"");
        StringAssert.Contains(createCustomer.Body, "\"email\":\"demouser@microsoft.com\"");
    }

    [TestMethod]
    public async Task SecondSubscribeReturnsExistingWithoutCreatingDuplicates()
    {
        var handler = MaxioTestServer.ForSubscribeFlow();
        using var factory = MaxioTestServer.CreateFactory(handler);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var first = await client.PostAsync("api/subscriptions", SubscribeContent());
        var second = await client.PostAsync("api/subscriptions", SubscribeContent());

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var model = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(model?.Subscription);
        Assert.IsTrue(model.AlreadyExisted);
        Assert.AreEqual(9001, model.Subscription.Id);

        // The double subscribe never produced a second customer or subscription upstream.
        Assert.AreEqual(1, handler.CountRequests(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(1, handler.CountRequests(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task ProviderRejectionSurfacesAs422()
    {
        var handler = MaxioTestServer.ForSubscribeFlow(failSubscriptionCreateWith422: true);
        using var factory = MaxioTestServer.CreateFactory(handler);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", SubscribeContent());

        Assert.AreEqual((HttpStatusCode)422, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "payment method");
    }

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        using var factory = MaxioTestServer.CreateFactory(MaxioTestServer.ForSubscribeFlow());
        var client = factory.CreateClient();

        var response = await client.PostAsync("api/subscriptions", SubscribeContent());

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static StringContent SubscribeContent() =>
        new(JsonSerializer.Serialize(new { productHandle = "eshop-pro" }), Encoding.UTF8, "application/json");
}
