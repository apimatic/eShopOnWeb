using Microsoft.eShopWeb.PublicApi.Stax;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentEndpointsTests
{
    private static HttpClient ClientFor(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [TestMethod]
    [DataRow("GET", "api/payment-methods")]
    [DataRow("POST", "api/orders")]
    [DataRow("GET", "api/orders/1")]
    [DataRow("POST", "api/orders/1/pay")]
    [DataRow("POST", "api/orders/1/checkout")]
    [DataRow("POST", "api/account/close")]
    public async Task ShopperRoutesNeedAToken(string method, string path)
    {
        var response = await ProgramTest.NewClient.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { })
        });
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    [DataRow("GET", "api/admin/shoppers/demouser@microsoft.com/payment-methods")]
    [DataRow("GET", "api/admin/orders/1")]
    [DataRow("POST", "api/admin/orders/1/refunds")]
    [DataRow("POST", "api/admin/invoice-schedules")]
    [DataRow("POST", "api/admin/invoice-schedules/00000000-0000-0000-0000-000000000000/pause")]
    [DataRow("POST", "api/admin/invoice-schedules/00000000-0000-0000-0000-000000000000/resume")]
    [DataRow("POST", "api/admin/sellers")]
    public async Task StaffRoutesAreForAdministrators(string method, string path)
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { })
        });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task InvoiceScheduleCannotStartInThePast()
    {
        var client = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var response = await client.PostAsJsonAsync("api/admin/invoice-schedules", new
        {
            customerEmail = "billing@example.com",
            company = "Example Co",
            amount = 10,
            firstInvoiceDate = "2001-01-01"
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SellersNeedAPlatformKey()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STAX_PLATFORM_API_KEY")))
            Assert.Inconclusive("A platform key is configured; this test must not create a real merchant.");

        var client = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var response = await client.PostAsJsonAsync("api/admin/sellers", new
        {
            company = "Seller Co",
            contactName = "Sam Seller",
            contactEmail = "sam@example.com"
        });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task OrdersAreNotVisibleToOtherShoppers()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var order = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, units = 1 } } });
        Assert.AreEqual(HttpStatusCode.Created, order.StatusCode);
        var orderId = Parse(await order.Content.ReadAsStringAsync()).GetProperty("orderId").GetInt32();

        var other = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var response = await other.GetAsync($"api/orders/{orderId}");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static JsonElement Card(string binType, string? state = null) => Parse(
        "{\"method\":\"card\",\"bin_type\":\"" + binType + "\"" + (state is null ? "" : ",\"address_state\":\"" + state + "\"") + "}");

    [TestMethod]
    public void CardFeeIsPassedOnToTheShopper()
    {
        var settings = new SurchargeSettings(true, 3m, "state_check");
        Assert.AreEqual(3.00m, OrderPaymentService.ComputeFee(100m, Card("CREDIT"), settings));
        Assert.AreEqual(1.50m, OrderPaymentService.ComputeFee(49.99m, Card("CREDIT"), settings)); // 1.4997 rounded
        Assert.AreEqual(0.03m, OrderPaymentService.ComputeFee(1m, Card("CREDIT"), new SurchargeSettings(true, 0.03m, null)));
    }

    [TestMethod]
    public void NoFeeForBankDebitOrWhenSurchargingIsOff()
    {
        var on = new SurchargeSettings(true, 3m, "state_check");
        Assert.AreEqual(0m, OrderPaymentService.ComputeFee(100m, Parse("{\"method\":\"bank\"}"), on));
        Assert.AreEqual(0m, OrderPaymentService.ComputeFee(100m, Card("DEBIT"), on));
        Assert.AreEqual(0m, OrderPaymentService.ComputeFee(100m, Card("CREDIT", "MA"), on));
        Assert.AreEqual(0m, OrderPaymentService.ComputeFee(100m, Card("CREDIT"), new SurchargeSettings(false, 3m, null)));
        Assert.AreEqual(0m, OrderPaymentService.ComputeFee(100m, Card("CREDIT"), new SurchargeSettings(true, null, null)));
    }

    /// <summary>Runs against the Stax sandbox, with the credentials from the "Stax:" settings (user-secrets).</summary>
    [TestMethod]
    public async Task ShopperPaysACardOrderAndStaffRefundsIt()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());

        var saved = await shopper.PostAsJsonAsync("api/payment-methods", new
        {
            method = "card",
            personName = "Test Shopper",
            cardNumber = "4111111111111111",
            cardExp = "1230",
            cardCvv = "123"
        });
        if (saved.StatusCode == HttpStatusCode.ServiceUnavailable)
            Assert.Inconclusive("The Stax sandbox credentials are not configured.");
        Assert.AreEqual(HttpStatusCode.Created, saved.StatusCode, await saved.Content.ReadAsStringAsync());
        var methodId = Parse(await saved.Content.ReadAsStringAsync()).GetProperty("id").GetString();

        var order = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, units = 2 } } });
        var orderId = Parse(await order.Content.ReadAsStringAsync()).GetProperty("orderId").GetInt32();

        var checkout = await shopper.PostAsJsonAsync($"api/orders/{orderId}/checkout", new { paymentMethodId = methodId });
        Assert.AreEqual(HttpStatusCode.OK, checkout.StatusCode);
        var quote = Parse(await checkout.Content.ReadAsStringAsync());
        Assert.AreEqual(quote.GetProperty("subtotal").GetDecimal() + quote.GetProperty("fee").GetDecimal(), quote.GetProperty("total").GetDecimal());

        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId = methodId });
        Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode, await pay.Content.ReadAsStringAsync());
        var paid = Parse(await pay.Content.ReadAsStringAsync());
        Assert.IsTrue(paid.GetProperty("paid").GetBoolean());
        Assert.AreEqual(quote.GetProperty("total").GetDecimal(), paid.GetProperty("total").GetDecimal());

        var again = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId = methodId });
        Assert.AreEqual(HttpStatusCode.Conflict, again.StatusCode);

        var status = Parse(await shopper.GetStringAsync($"api/orders/{orderId}"));
        Assert.IsTrue(status.GetProperty("paid").GetBoolean());

        var refund = await admin.PostAsync($"api/admin/orders/{orderId}/refunds", null);
        Assert.IsTrue(refund.IsSuccessStatusCode, await refund.Content.ReadAsStringAsync());
        Assert.AreEqual("refunded", Parse(await refund.Content.ReadAsStringAsync()).GetProperty("refundStatus").GetString());
        var staffView = Parse(await admin.GetStringAsync($"api/admin/orders/{orderId}"));
        Assert.AreEqual("refunded", staffView.GetProperty("refundStatus").GetString());

        var removed = await shopper.DeleteAsync($"api/payment-methods/{methodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, removed.StatusCode);
        var support = Parse(await admin.GetStringAsync("api/admin/shoppers/demouser@microsoft.com/payment-methods"));
        var entry = support.EnumerateArray().Single(m => m.GetProperty("id").GetString() == methodId);
        Assert.IsTrue(entry.GetProperty("removed").GetBoolean());
        Assert.AreNotEqual(JsonValueKind.Null, entry.GetProperty("removedAt").ValueKind);
    }
}
