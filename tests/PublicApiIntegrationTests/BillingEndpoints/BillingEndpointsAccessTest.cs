using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// Access rules and request validation of the billing endpoints. None of these reach Maxio:
/// each is answered before the first billing-system call.
/// </summary>
[TestClass]
public class BillingEndpointsAccessTest
{
    private static HttpClient ClientFor(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token != null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task SubscribeRequiresSignIn()
    {
        var response = await ClientFor(null).PostAsync("api/subscriptions", Json("{\"planHandle\":\"basic\"}"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionPageRequiresSignIn()
    {
        var response = await ClientFor(null).GetAsync("api/subscriptions/1");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task StaffRoutesRefuseShoppers()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());

        var createPlan = await shopper.PostAsync("api/admin/plans", Json("{\"name\":\"x\",\"price\":1,\"brand\":\"Azure\"}"));
        var currency = await shopper.PostAsync("api/admin/plans/1/currency-prices", Json("{\"currency\":\"EUR\",\"price\":9}"));
        var billingDate = await shopper.PutAsync("api/admin/subscriptions/1/next-billing-date", Json("{\"nextBillingDate\":\"2099-01-01\"}"));

        Assert.AreEqual(HttpStatusCode.Forbidden, createPlan.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, currency.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, billingDate.StatusCode);
    }

    [TestMethod]
    public async Task StaffRoutesRefuseAnonymousCallers()
    {
        var response = await ClientFor(null).PostAsync("api/admin/plans", Json("{\"name\":\"x\",\"price\":1,\"brand\":\"Azure\"}"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task UsageNeedsAWellFormedDate()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());

        var missing = await shopper.GetAsync("api/subscriptions/1/usage");
        var malformed = await shopper.GetAsync("api/subscriptions/1/usage?date=12-11-2026");

        Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    [TestMethod]
    public async Task NewPlanNeedsANameAndAPrice()
    {
        var staff = ClientFor(ApiTokenHelper.GetAdminUserToken());

        var noName = await staff.PostAsync("api/admin/plans", Json("{\"price\":5,\"brand\":\"Azure\"}"));
        var negative = await staff.PostAsync("api/admin/plans", Json("{\"name\":\"x\",\"price\":-1,\"brand\":\"Azure\"}"));
        var fraction = await staff.PostAsync("api/admin/plans", Json("{\"name\":\"x\",\"price\":1.999,\"brand\":\"Azure\"}"));

        Assert.AreEqual(HttpStatusCode.BadRequest, noName.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, negative.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, fraction.StatusCode);
    }

    [TestMethod]
    public async Task CurrencyPriceNeedsAnIsoCode()
    {
        var staff = ClientFor(ApiTokenHelper.GetAdminUserToken());

        var response = await staff.PostAsync("api/admin/plans/1/currency-prices", Json("{\"currency\":\"EURO\",\"price\":9}"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task NextBillingDateIsRequired()
    {
        var staff = ClientFor(ApiTokenHelper.GetAdminUserToken());

        var response = await staff.PutAsync("api/admin/subscriptions/1/next-billing-date", Json("{}"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
