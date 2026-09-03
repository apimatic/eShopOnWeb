using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentEndpointTests
{
    [TestMethod]
    public async Task PlaceOrderUsesAuthenticatedBuyerAndReturnsTopLevelIdentifier()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());
        var body = JsonSerializer.Serialize(new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } },
            shippingAddress = new
            {
                street = "1 Test Street", city = "Seattle", state = "WA",
                country = "US", postalCode = "98101"
            }
        });

        var response = await client.PostAsync("api/orders",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(json.RootElement.GetProperty("orderId").GetInt32() > 0);
        Assert.AreEqual(39m, json.RootElement.GetProperty("order").GetProperty("total").GetDecimal());
        Assert.AreEqual("USD", json.RootElement.GetProperty("order").GetProperty("currency").GetString());
        Assert.AreEqual(0, json.RootElement.GetProperty("order").GetProperty("paymentStatus").GetInt32());
    }

    [TestMethod]
    public async Task ShopperCannotInvokeOperatorEndpoints()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var fulfil = await client.PostAsync("api/orders/1/fulfil",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        var reconciliation = await client.GetAsync(
            "api/reconciliation?from=2026-01-01T00%3A00%3A00Z&to=2026-01-02T00%3A00%3A00Z");

        Assert.AreEqual(HttpStatusCode.Forbidden, fulfil.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, reconciliation.StatusCode);
    }

    [TestMethod]
    public async Task PaymentMethodsRequireAuthentication()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/payment-methods");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
