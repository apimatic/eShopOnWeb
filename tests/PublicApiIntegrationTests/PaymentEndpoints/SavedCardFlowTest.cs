using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class SavedCardFlowTest
{
    private static readonly PaymentApiFactory _factory = new();

    private static HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private const string SaveCardJson =
        "{\"card\":{\"number\":\"4111111111111111\",\"expiryMonth\":11,\"expiryYear\":2031,\"securityCode\":\"123\",\"cardholderName\":\"Test Holder\"}}";

    [TestMethod]
    public async Task SaveReuseAndDelete_Works()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());

        // Save
        var saveResp = await shopper.PostAsync("api/payment-methods", Json(SaveCardJson));
        Assert.AreEqual(HttpStatusCode.Created, saveResp.StatusCode);
        int methodId;
        using (var doc = JsonDocument.Parse(await saveResp.Content.ReadAsStringAsync()))
        {
            methodId = doc.RootElement.GetProperty("paymentMethodId").GetInt32();
            var method = doc.RootElement.GetProperty("paymentMethod");
            Assert.AreEqual("1111", method.GetProperty("lastFourDigits").GetString());
            // The full number is never returned.
            Assert.IsFalse((await saveResp.Content.ReadAsStringAsync()).Contains("4111111111111111"));
        }

        // List includes it
        var listResp = await shopper.GetAsync("api/payment-methods");
        using (var doc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync()))
        {
            var methods = doc.RootElement.GetProperty("paymentMethods");
            Assert.IsTrue(methods.GetArrayLength() >= 1);
        }

        // Reuse it to pay an order
        var orderResp = await shopper.PostAsync("api/orders",
            Json("{\"items\":[{\"catalogItemId\":3,\"quantity\":1}]}"));
        int orderId;
        using (var doc = JsonDocument.Parse(await orderResp.Content.ReadAsStringAsync()))
        {
            orderId = doc.RootElement.GetProperty("orderId").GetInt32();
        }

        var payResp = await shopper.PostAsync($"api/orders/{orderId}/pay",
            Json($"{{\"savedPaymentMethodId\":{methodId}}}"));
        Assert.AreEqual(HttpStatusCode.OK, payResp.StatusCode);
        using (var doc = JsonDocument.Parse(await payResp.Content.ReadAsStringAsync()))
        {
            var payment = doc.RootElement.GetProperty("payment");
            Assert.AreEqual("Authorized", payment.GetProperty("status").GetString());
            Assert.AreEqual(methodId, payment.GetProperty("savedPaymentMethodId").GetInt32());
        }

        // Delete
        var delResp = await shopper.DeleteAsync($"api/payment-methods/{methodId}");
        Assert.AreEqual(HttpStatusCode.OK, delResp.StatusCode);

        // No longer usable
        var order2Resp = await shopper.PostAsync("api/orders",
            Json("{\"items\":[{\"catalogItemId\":2,\"quantity\":1}]}"));
        int order2Id;
        using (var doc = JsonDocument.Parse(await order2Resp.Content.ReadAsStringAsync()))
        {
            order2Id = doc.RootElement.GetProperty("orderId").GetInt32();
        }

        var payDeletedResp = await shopper.PostAsync($"api/orders/{order2Id}/pay",
            Json($"{{\"savedPaymentMethodId\":{methodId}}}"));
        Assert.AreEqual(HttpStatusCode.NotFound, payDeletedResp.StatusCode);
    }

    [TestMethod]
    public async Task DeleteAnotherUsersCard_IsNotFound()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());

        var saveResp = await shopper.PostAsync("api/payment-methods", Json(SaveCardJson));
        int methodId;
        using (var doc = JsonDocument.Parse(await saveResp.Content.ReadAsStringAsync()))
        {
            methodId = doc.RootElement.GetProperty("paymentMethodId").GetInt32();
        }

        // A different identity cannot delete it.
        var delResp = await admin.DeleteAsync($"api/payment-methods/{methodId}");
        Assert.AreEqual(HttpStatusCode.NotFound, delResp.StatusCode);
    }
}
