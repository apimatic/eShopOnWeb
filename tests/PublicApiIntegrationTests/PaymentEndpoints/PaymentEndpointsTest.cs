using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentEndpointsTest
{
    private static HttpClient Client(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static bool HasStaxCredentials =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STAX_API_KEY"));

    [TestMethod]
    [DataRow("GET", "api/payment-methods")]
    [DataRow("GET", "api/orders/1")]
    [DataRow("POST", "api/account/close")]
    public async Task ShopperEndpointsRequireAToken(string method, string url)
    {
        var response = await Client(null).SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    [DataRow("GET", "api/admin/orders/1")]
    [DataRow("POST", "api/admin/orders/1/refunds")]
    [DataRow("GET", "api/admin/shoppers/a@b.com/payment-methods")]
    [DataRow("POST", "api/admin/invoice-schedules")]
    [DataRow("POST", "api/admin/sellers")]
    public async Task StaffEndpointsRejectShoppers(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST") request.Content = JsonContent.Create(new { });
        var response = await Client(ApiTokenHelper.GetNormalUserToken()).SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task OrderForUnknownCatalogItemIsRejected()
    {
        var response = await Client(ApiTokenHelper.GetNormalUserToken())
            .PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 99999, units = 1 } } });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task InvalidInvoiceScheduleIsRejected()
    {
        var response = await Client(ApiTokenHelper.GetAdminUserToken())
            .PostAsJsonAsync("api/admin/invoice-schedules", new { email = "not-an-email", company = "", amount = -5 });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SellerOnboardingWithoutPlatformKeyIsRejected()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STAX_PLATFORM_API_KEY")))
            Assert.Inconclusive("A platform key is configured.");
        var response = await Client(ApiTokenHelper.GetAdminUserToken())
            .PostAsJsonAsync("api/admin/sellers", new { company = "Seller Co", contact = new { name = "Sam", email = "sam@seller.example" } });
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperCanSaveACardCheckoutAndPayAgainstTheSandbox()
    {
        if (!HasStaxCredentials) Assert.Inconclusive("STAX_API_KEY is not set.");
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var saved = await client.PostAsJsonAsync("api/payment-methods",
            new { method = "card", personName = "Demo User", cardNumber = "4111111111111111", cardExp = "1230", cardCvv = "123" });
        Assert.AreEqual(HttpStatusCode.Created, saved.StatusCode, await saved.Content.ReadAsStringAsync());
        var methodId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var order = await client.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 2, units = 2 } } });
        Assert.AreEqual(HttpStatusCode.Created, order.StatusCode);
        var orderId = (await order.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetInt32();

        var checkout = await client.PostAsJsonAsync($"api/orders/{orderId}/checkout", new { paymentMethodId = methodId });
        checkout.EnsureSuccessStatusCode();
        var quote = await checkout.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(quote.GetProperty("amount").GetDecimal() + quote.GetProperty("fee").GetDecimal(), quote.GetProperty("total").GetDecimal());
        Assert.IsTrue(quote.GetProperty("fee").GetDecimal() > 0, "a credit card carries the processing fee");

        var paid = await client.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId = methodId });
        paid.EnsureSuccessStatusCode();
        Assert.IsTrue((await paid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paid").GetBoolean());

        var again = await client.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId = methodId });
        Assert.AreEqual(HttpStatusCode.Conflict, again.StatusCode);

        var refund = await Client(ApiTokenHelper.GetAdminUserToken()).PostAsync($"api/admin/orders/{orderId}/refunds", null);
        refund.EnsureSuccessStatusCode();
        Assert.AreEqual("refunded", (await refund.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refundStatus").GetString());

        var removed = await client.DeleteAsync($"api/payment-methods/{methodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, removed.StatusCode);
    }
}
