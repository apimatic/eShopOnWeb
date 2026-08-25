using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class OrderPaymentAuthorizationTests
{
    [TestMethod]
    public async Task FulfilOrderReturnsForbiddenForNormalUser()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/orders/1/fulfil", null);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task CancelOrderReturnsForbiddenForNormalUser()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/orders/1/cancel", null);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ReconciliationReturnsForbiddenForNormalUser()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/reconciliation?from=2020-01-01T00:00:00Z&to=2020-01-02T00:00:00Z");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task MyOrdersReturnsUnauthorizedWithoutAToken()
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync("api/my-orders");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PaymentMethodsReturnsUnauthorizedWithoutAToken()
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync("api/payment-methods");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PayOrderReturnsNotFoundForNonexistentOrder()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var content = new System.Net.Http.StringContent(
            "{\"savedPaymentMethodId\": 999999}", System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/orders/999999/pay", content);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
