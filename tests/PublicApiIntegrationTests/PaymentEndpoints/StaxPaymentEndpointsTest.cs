using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// These tests talk to the real Stax sandbox: they need the Stax:ApiKey user-secret (loaded from STAX_API_KEY).
/// </summary>
[TestClass]
public class StaxPaymentEndpointsTest
{
    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static readonly object CardBody = new { method = "card", personName = "Test Shopper", cardNumber = "4111111111111111", cardExpiry = "1230", cardCvv = "123" };

    [TestMethod]
    public async Task EndpointsRequireAuthentication()
    {
        var client = ProgramTest.NewClient;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/payment-methods")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/orders", new { })).StatusCode);
    }

    [TestMethod]
    public async Task StaffEndpointsRejectShoppers()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("/api/admin/orders/1")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync("/api/admin/orders/1/refunds", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("/api/admin/shoppers/a@b.com/payment-methods")).StatusCode);
    }

    [TestMethod]
    public async Task SavedMethodCanBeListedAndRemovedAndStaffStillSeeIt()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var created = await shopper.PostAsJsonAsync("/api/payment-methods", CardBody);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetString()!;

        var list = await Json(await shopper.GetAsync("/api/payment-methods"));
        Assert.IsTrue(list.EnumerateArray().Any(p => p.GetProperty("id").GetString() == id));

        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"/api/payment-methods/{id}")).StatusCode);
        list = await Json(await shopper.GetAsync("/api/payment-methods"));
        Assert.IsFalse(list.EnumerateArray().Any(p => p.GetProperty("id").GetString() == id));
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.DeleteAsync($"/api/payment-methods/{id}")).StatusCode);

        var staff = Client(ApiTokenHelper.GetAdminUserToken());
        var all = await Json(await staff.GetAsync("/api/admin/shoppers/demouser@microsoft.com/payment-methods"));
        var removed = all.EnumerateArray().First(p => p.GetProperty("id").GetString() == id);
        Assert.AreNotEqual(JsonValueKind.Null, removed.GetProperty("removedAt").ValueKind);
    }

    [TestMethod]
    public async Task CardOrderCanBeCheckedOutPaidAndRefunded()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var pmId = (await Json(await shopper.PostAsJsonAsync("/api/payment-methods", CardBody))).GetProperty("id").GetString()!;

        var orderResponse = await shopper.PostAsJsonAsync("/api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 2 } } });
        Assert.AreEqual(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = await Json(orderResponse);
        var orderId = order.GetProperty("orderId").GetInt32();
        Assert.IsFalse(order.GetProperty("paid").GetBoolean());

        var checkout = await Json(await shopper.PostAsJsonAsync($"/api/orders/{orderId}/checkout", new { paymentMethodId = pmId }));
        Assert.AreEqual(checkout.GetProperty("subtotal").GetDecimal() + checkout.GetProperty("fee").GetDecimal(), checkout.GetProperty("total").GetDecimal());

        var pay = await shopper.PostAsJsonAsync($"/api/orders/{orderId}/pay", new { paymentMethodId = pmId });
        Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode);
        Assert.IsTrue((await Json(pay)).GetProperty("paid").GetBoolean());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsJsonAsync($"/api/orders/{orderId}/pay", new { paymentMethodId = pmId })).StatusCode);

        var staff = Client(ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.NotFound, (await staff.GetAsync("/api/orders/" + orderId)).StatusCode);
        var refund = await staff.PostAsync($"/api/admin/orders/{orderId}/refunds", null);
        Assert.IsTrue(refund.IsSuccessStatusCode);
        Assert.AreEqual("refunded", (await Json(refund)).GetProperty("refundStatus").GetString());
        var view = await Json(await staff.GetAsync($"/api/admin/orders/{orderId}"));
        Assert.AreEqual("refunded", view.GetProperty("refundStatus").GetString());
    }

    [TestMethod]
    public async Task BankPaymentStaysUnpaidUntilTheBankSettlesAndItsRefundWaits()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var pmId = (await Json(await shopper.PostAsJsonAsync("/api/payment-methods", new
        {
            method = "bank", personName = "Test Shopper", bankAccount = "9876543210", bankRouting = "021000021", bankType = "checking"
        }))).GetProperty("id").GetString()!;
        var orderId = (await Json(await shopper.PostAsJsonAsync("/api/orders", new { items = new[] { new { catalogItemId = 2, quantity = 1 } } }))).GetProperty("orderId").GetInt32();

        var pay = await Json(await shopper.PostAsJsonAsync($"/api/orders/{orderId}/pay", new { paymentMethodId = pmId }));
        Assert.IsFalse(pay.GetProperty("paid").GetBoolean());

        var staff = Client(ApiTokenHelper.GetAdminUserToken());
        var refund = await staff.PostAsync($"/api/admin/orders/{orderId}/refunds", null);
        Assert.AreEqual("pending", (await Json(refund)).GetProperty("refundStatus").GetString());
    }

    [TestMethod]
    public async Task ShopperCannotUseSomeoneElsesOrder()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var orderId = (await Json(await shopper.PostAsJsonAsync("/api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } }))).GetProperty("orderId").GetInt32();
        var other = Client(ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.GetAsync($"/api/orders/{orderId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/orders/{orderId}/pay", new { paymentMethodId = "x" })).StatusCode);
    }

    [TestMethod]
    public async Task InvalidOrdersAreRefusedWithAReason()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.PostAsJsonAsync("/api/orders", new { items = new object[0] })).StatusCode);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, (await shopper.PostAsJsonAsync("/api/orders", new { items = new[] { new { catalogItemId = 999999, quantity = 1 } } })).StatusCode);
    }

    [TestMethod]
    public async Task MonthlyInvoiceCanBePausedAndResumed()
    {
        var staff = Client(ApiTokenHelper.GetAdminUserToken());
        var created = await staff.PostAsJsonAsync("/api/admin/invoice-schedules", new { email = "billing@acme-test.example.com", company = "Acme Inc", amount = 125.50m });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("scheduleId").GetString();

        var paused = await Json(await staff.PostAsync($"/api/admin/invoice-schedules/{id}/pause", null));
        Assert.IsFalse(paused.GetProperty("active").GetBoolean());
        var resumed = await Json(await staff.PostAsync($"/api/admin/invoice-schedules/{id}/resume", null));
        Assert.IsTrue(resumed.GetProperty("active").GetBoolean());
    }
}
