using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class OrderEndpointTests
{
    [TestMethod]
    public async Task CreatingOrderRequiresAuthenticationAndReturnsTopLevelId()
    {
        var body = Json();
        var anonymous = ProgramTest.NewClient;
        var unauthorized = await anonymous.PostAsync("api/orders", body);
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders", Json());
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(document.RootElement.TryGetProperty("orderId", out var orderId));
        Assert.IsTrue(orderId.GetInt32() > 0);
    }

    [TestMethod]
    public async Task OperatorEndpointsRejectShopperToken()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var fulfil = await client.PostAsync("api/orders/1/fulfil",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        var cancel = await client.PostAsync("api/orders/1/cancel",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        var reconciliation = await client.GetAsync(
            "api/reconciliation?from=2026-01-01T00%3A00%3A00Z&to=2026-01-02T00%3A00%3A00Z");

        Assert.AreEqual(HttpStatusCode.Forbidden, fulfil.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, cancel.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, reconciliation.StatusCode);
    }

    private static StringContent Json() => new(JsonSerializer.Serialize(new
    {
        items = new[] { new { catalogItemId = 1, quantity = 1 } },
        shippingAddress = new
        {
            street = "1 Main Street",
            city = "Seattle",
            state = "WA",
            country = "US",
            postalCode = "98101"
        }
    }), Encoding.UTF8, "application/json");
}
