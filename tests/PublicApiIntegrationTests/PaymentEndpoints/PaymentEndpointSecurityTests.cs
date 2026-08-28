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
public class PaymentEndpointSecurityTests
{
    [TestMethod]
    public async Task PaymentEndpointsRequireJwtAndOperatorRoutesRequireAdministrator()
    {
        var anonymous = ProgramTest.NewClient;
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/my-orders")).StatusCode);

        var shopper = ProgramTest.NewClient;
        shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PostAsync("/api/orders/999/fulfil", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PostAsync("/api/orders/999/cancel", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.GetAsync("/api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z")).StatusCode);
    }

    [TestMethod]
    public async Task CreatedOrderIsVisibleOnlyToItsOwnerAndReturnsTopLevelId()
    {
        var shopper = ProgramTest.NewClient;
        shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetNormalUserToken());
        using var content = new StringContent(
            "{\"items\":[{\"catalogItemId\":1,\"quantity\":2}]}", Encoding.UTF8, "application/json");
        var create = await shopper.PostAsync("/api/orders", content);
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var orderId = created.RootElement.GetProperty("orderId").GetInt32();
        Assert.IsTrue(orderId > 0);
        Assert.AreEqual("AwaitingPayment", created.RootElement.GetProperty("paymentStatus").GetString());

        using var mine = JsonDocument.Parse(await shopper.GetStringAsync("/api/my-orders"));
        Assert.IsTrue(mine.RootElement.EnumerateArray()
            .Any(order => order.GetProperty("orderId").GetInt32() == orderId));

        var administrator = ProgramTest.NewClient;
        administrator.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiTokenHelper.GetAdminUserToken());
        using var administratorsOrders = JsonDocument.Parse(
            await administrator.GetStringAsync("/api/my-orders"));
        Assert.IsFalse(administratorsOrders.RootElement.EnumerateArray()
            .Any(order => order.GetProperty("orderId").GetInt32() == orderId));
    }
}
