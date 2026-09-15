using System.Linq;
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
    public async Task OrderIsJwtProtectedShopperScopedAndStartsAwaitingPayment()
    {
        var anonymous = ProgramTest.NewClient;
        var unauthorized = await anonymous.PostAsync("api/orders", OrderContent());
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var shopper = ProgramTest.NewClient;
        shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());
        var created = await shopper.PostAsync("api/orders", OrderContent());
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var orderId = createdJson.RootElement.GetProperty("orderId").GetInt32();
        Assert.IsTrue(orderId > 0);
        Assert.AreEqual("AwaitingPayment",
            createdJson.RootElement.GetProperty("payment").GetProperty("status").GetString());

        var forbidden = await shopper.PostAsync($"api/orders/{orderId}/fulfil",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var otherUser = ProgramTest.NewClient;
        otherUser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetAdminUserToken());
        var otherOrders = await otherUser.GetAsync("api/my-orders");
        otherOrders.EnsureSuccessStatusCode();
        using var otherJson = JsonDocument.Parse(await otherOrders.Content.ReadAsStringAsync());
        Assert.IsFalse(otherJson.RootElement.EnumerateArray().Any(x =>
            x.GetProperty("orderId").GetInt32() == orderId));
    }

    private static StringContent OrderContent() => new("""
        {"items":[{"catalogItemId":1,"quantity":1}],"shippingAddress":{"street":"1 Main St","city":"Seattle","state":"WA","country":"US","zipCode":"98101"}}
        """, Encoding.UTF8, "application/json");
}
