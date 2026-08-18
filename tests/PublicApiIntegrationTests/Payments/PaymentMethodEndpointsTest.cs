using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class PaymentMethodEndpointsTest
{
    private static readonly PaymentApiFactory _factory = new();

    private static HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private const string SaveCardJson =
        "{\"card\":{\"number\":\"4111111111111111\",\"expiry\":\"2030-01\",\"securityCode\":\"123\",\"cardholderName\":\"A\",\"billingAddress\":{\"countryCode\":\"US\"}}}";

    private static async Task<int> CardCountAsync(HttpClient client)
    {
        var resp = await client.GetAsync("api/payment-methods");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("paymentMethods").GetArrayLength();
    }

    [TestMethod]
    public async Task Save_List_Delete_And_UnusableAfterDelete()
    {
        var shopper = Client(TestTokens.Shopper("cards-user@test.com"));

        var save = await shopper.PostAsync("api/payment-methods", Json(SaveCardJson));
        Assert.AreEqual(HttpStatusCode.Created, save.StatusCode);
        using var saved = JsonDocument.Parse(await save.Content.ReadAsStringAsync());
        var pmId = saved.RootElement.GetProperty("paymentMethodId").GetInt32();
        Assert.AreEqual("1111", saved.RootElement.GetProperty("lastDigits").GetString());
        Assert.AreEqual("VISA", saved.RootElement.GetProperty("brand").GetString());
        // The safe descriptor must never carry the full PAN.
        Assert.IsFalse((await save.Content.ReadAsStringAsync()).Contains("4111111111111111"));

        Assert.AreEqual(1, await CardCountAsync(shopper));

        // Delete
        var del = await shopper.DeleteAsync($"api/payment-methods/{pmId}");
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);
        Assert.AreEqual(0, await CardCountAsync(shopper));

        // No longer usable to pay
        var orderResp = await shopper.PostAsync("api/orders", Json("{\"items\":[{\"catalogItemId\":1,\"quantity\":1}]}"));
        var orderId = JsonDocument.Parse(await orderResp.Content.ReadAsStringAsync()).RootElement.GetProperty("orderId").GetInt32();
        var pay = await shopper.PostAsync($"api/orders/{orderId}/pay", Json($"{{\"savedPaymentMethodId\":{pmId}}}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, pay.StatusCode);
    }

    [TestMethod]
    public async Task Reuse_SavedCard_ToPay()
    {
        var shopper = Client(TestTokens.Shopper("reuse-user@test.com"));

        var save = await shopper.PostAsync("api/payment-methods", Json(SaveCardJson));
        var pmId = JsonDocument.Parse(await save.Content.ReadAsStringAsync()).RootElement.GetProperty("paymentMethodId").GetInt32();

        var orderResp = await shopper.PostAsync("api/orders", Json("{\"items\":[{\"catalogItemId\":1,\"quantity\":1}]}"));
        var orderId = JsonDocument.Parse(await orderResp.Content.ReadAsStringAsync()).RootElement.GetProperty("orderId").GetInt32();

        var pay = await shopper.PostAsync($"api/orders/{orderId}/pay", Json($"{{\"savedPaymentMethodId\":{pmId}}}"));
        Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode);
    }

    [TestMethod]
    public async Task OneShopper_CannotDeleteAnothersCard()
    {
        var owner = Client(TestTokens.Shopper("owner@test.com"));
        var other = Client(TestTokens.Shopper("other@test.com"));

        var save = await owner.PostAsync("api/payment-methods", Json(SaveCardJson));
        var pmId = JsonDocument.Parse(await save.Content.ReadAsStringAsync()).RootElement.GetProperty("paymentMethodId").GetInt32();

        // Another shopper deleting it gets 404 and the owner's card remains.
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.DeleteAsync($"api/payment-methods/{pmId}")).StatusCode);
        Assert.AreEqual(0, await CardCountAsync(other));
        Assert.AreEqual(1, await CardCountAsync(owner));
    }
}
