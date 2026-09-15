using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

[TestClass]
public class PaymentEndpointAuthorizationTests
{
    [TestMethod]
    public async Task ShopperCannotFulfilCancelOrReconcile()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var fulfil = await client.PostAsync("api/orders/1/fulfil", Json());
        var cancel = await client.PostAsync("api/orders/1/cancel", Json());
        var reconciliation = await client.GetAsync("api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z");

        Assert.AreEqual(HttpStatusCode.Forbidden, fulfil.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, cancel.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, reconciliation.StatusCode);
    }

    [TestMethod]
    public async Task AnonymousCallerCannotUseShopperPaymentRoutes()
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync("api/my-orders");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static StringContent Json() => new("{}", Encoding.UTF8, "application/json");
}
