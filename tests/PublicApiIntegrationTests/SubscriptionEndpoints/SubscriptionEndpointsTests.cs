using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTests
{
    private static SubscriptionTestApplication _application = new SubscriptionTestApplication();
    private static HttpClient _client = _application.CreateClient();

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _application.Dispose();
    }

    private static HttpRequestMessage AuthorizedRequest(HttpMethod method, string url, string? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return request;
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var response = await _client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        var post = await _client.PostAsync("api/subscriptions",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, post.StatusCode);

        var mine = await _client.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, mine.StatusCode);
    }

    [TestMethod]
    public async Task ListsSubscriptionPlansForAuthenticatedUser()
    {
        _application.Fake.ExceptionToThrow = null;

        var response = await _client.SendAsync(AuthorizedRequest(HttpMethod.Get, "api/subscription-plans"));
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual(2, model!.SubscriptionPlans.Count);
        Assert.AreEqual("eshop-pro", model.SubscriptionPlans[0].Handle);
        Assert.AreEqual(299.00m, model.SubscriptionPlans[0].Price);
        Assert.IsTrue(model.SubscriptionPlans[0].IsDefault);
    }

    [TestMethod]
    public async Task SubscribeCreatesSubscriptionAndEchoesPlanAndState()
    {
        _application.Fake.ExceptionToThrow = null;
        _application.Fake.Subscriptions.Clear();

        var body = JsonSerializer.Serialize(new CreateSubscriptionRequest { PlanHandle = "eshop-pro", FirstName = "Demo", LastName = "User" });
        var response = await _client.SendAsync(AuthorizedRequest(HttpMethod.Post, "api/subscriptions", body));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(model);
        Assert.IsTrue(model!.Created);
        Assert.IsNotNull(model.Subscription);
        Assert.AreEqual("eshop-pro", model.Subscription!.PlanHandle);
        Assert.AreEqual(299.00m, model.Subscription.Price);
        Assert.AreEqual("active", model.Subscription.State);
        Assert.IsNotNull(model.Subscription.NextBillingDate);

        // The Maxio customer reference is derived from the authenticated user.
        Assert.IsTrue(_application.Fake.LastCustomerReference!.StartsWith("eshop-"));
        Assert.AreEqual("eshop-pro", _application.Fake.LastPlanHandle);
    }

    [TestMethod]
    public async Task SubscribeIsIdempotentForDoubleSubmit()
    {
        _application.Fake.ExceptionToThrow = null;
        _application.Fake.Subscriptions.Clear();

        var body = JsonSerializer.Serialize(new CreateSubscriptionRequest { PlanHandle = "basic-plan" });

        var first = await _client.SendAsync(AuthorizedRequest(HttpMethod.Post, "api/subscriptions", body));
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.SendAsync(AuthorizedRequest(HttpMethod.Post, "api/subscriptions", body));
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);

        var model = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsFalse(model!.Created);
        Assert.AreEqual("basic-plan", model.Subscription!.PlanHandle);
        Assert.AreEqual(1, _application.Fake.Subscriptions.Count);
    }

    [TestMethod]
    public async Task SubscribeRequiresPlanHandle()
    {
        _application.Fake.ExceptionToThrow = null;

        var response = await _client.SendAsync(AuthorizedRequest(HttpMethod.Post, "api/subscriptions", "{}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ErrorDetailsBody>();
        Assert.IsNotNull(model);
        StringAssert.Contains(model!.Message, "planHandle");
    }

    [TestMethod]
    public async Task ListsMySubscriptions()
    {
        _application.Fake.ExceptionToThrow = null;
        _application.Fake.Subscriptions.Clear();
        await _client.SendAsync(AuthorizedRequest(HttpMethod.Post, "api/subscriptions",
            JsonSerializer.Serialize(new CreateSubscriptionRequest { PlanHandle = "eshop-pro" })));

        var response = await _client.SendAsync(AuthorizedRequest(HttpMethod.Get, "api/my-subscriptions"));
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual(1, model!.Subscriptions.Count);
        Assert.AreEqual("eshop-pro", model.Subscriptions[0].PlanHandle);
        Assert.AreEqual("active", model.Subscriptions[0].State);
    }

    [TestMethod]
    public async Task MapsMaxioNotFoundTo404ErrorBody()
    {
        _application.Fake.ExceptionToThrow =
            new MaxioSubscriptionException(StatusCodes.Status404NotFound, "The requested subscription plan was not found.");

        var response = await _client.SendAsync(AuthorizedRequest(HttpMethod.Get, "api/subscription-plans"));
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ErrorDetailsBody>();
        Assert.AreEqual(404, model!.StatusCode);
        StringAssert.Contains(model.Message, "was not found");
    }

    private class ErrorDetailsBody
    {
        public int StatusCode { get; set; }

        public string Message { get; set; } = string.Empty;
    }
}
