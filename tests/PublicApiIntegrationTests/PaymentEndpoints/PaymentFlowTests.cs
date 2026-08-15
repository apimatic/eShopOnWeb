using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentFlowTests
{
    private static readonly PaymentApiFactory Factory = new();

    private static HttpClient ClientFor(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<int> FirstCatalogItemIdAsync(HttpClient client)
    {
        var resp = await client.GetAsync("api/catalog-items?pageSize=1&pageIndex=0");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("catalogItems")[0].GetProperty("id").GetInt32();
    }

    private static async Task<int> CreateOrderAsync(HttpClient shopper, int itemId)
    {
        var resp = await shopper.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = itemId, quantity = 1 } },
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.AreEqual("AwaitingPayment", doc.RootElement.GetProperty("status").GetString());
        return doc.RootElement.GetProperty("orderId").GetInt32();
    }

    private static object Card() => new
    {
        card = new { number = "4111111111111111", expiry = "2030-01", securityCode = "123", name = "Test Shopper", countryCode = "US" },
    };

    [TestMethod]
    public async Task Full_pay_fulfil_refund_flow_works_end_to_end()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());

        var itemId = await FirstCatalogItemIdAsync(shopper);
        var orderId = await CreateOrderAsync(shopper, itemId);

        // Authorize (hold).
        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", Card());
        pay.EnsureSuccessStatusCode();
        using var payDoc = JsonDocument.Parse(await pay.Content.ReadAsStringAsync());
        Assert.AreEqual("PaymentAuthorized", payDoc.RootElement.GetProperty("status").GetString());
        var authId = payDoc.RootElement.GetProperty("payment").GetProperty("authorizationId").GetString();
        Assert.IsFalse(string.IsNullOrEmpty(authId));

        // Idempotent: a second pay returns the same hold.
        var pay2 = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", Card());
        pay2.EnsureSuccessStatusCode();
        using var pay2Doc = JsonDocument.Parse(await pay2.Content.ReadAsStringAsync());
        Assert.AreEqual(authId, pay2Doc.RootElement.GetProperty("payment").GetProperty("authorizationId").GetString());

        // Fulfil (capture) — operator action.
        var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", null);
        fulfil.EnsureSuccessStatusCode();
        using var fulfilDoc = JsonDocument.Parse(await fulfil.Content.ReadAsStringAsync());
        Assert.AreEqual("Paid", fulfilDoc.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(fulfilDoc.RootElement.GetProperty("payment").GetProperty("captureId").GetString()));

        // Refund with idempotency.
        var refund = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1.00m, idempotencyKey = "key-1" });
        refund.EnsureSuccessStatusCode();
        using var refundDoc = JsonDocument.Parse(await refund.Content.ReadAsStringAsync());
        var refundId = refundDoc.RootElement.GetProperty("refundId").GetString();

        var refundAgain = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1.00m, idempotencyKey = "key-1" });
        refundAgain.EnsureSuccessStatusCode();
        using var refundAgainDoc = JsonDocument.Parse(await refundAgain.Content.ReadAsStringAsync());
        Assert.AreEqual(refundId, refundAgainDoc.RootElement.GetProperty("refundId").GetString());
    }

    [TestMethod]
    public async Task Fulfil_is_forbidden_for_a_shopper()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var itemId = await FirstCatalogItemIdAsync(shopper);
        var orderId = await CreateOrderAsync(shopper, itemId);

        var resp = await shopper.PostAsync($"api/orders/{orderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task A_shopper_cannot_act_on_another_shoppers_order()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());

        var itemId = await FirstCatalogItemIdAsync(shopper);
        var orderId = await CreateOrderAsync(shopper, itemId);

        // The admin, acting as its own (different) identity, cannot refund the shopper's order.
        var resp = await admin.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1.00m, idempotencyKey = "x" });
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task Saved_card_can_be_created_listed_used_and_deleted()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var itemId = await FirstCatalogItemIdAsync(shopper);

        // Save a card.
        var save = await shopper.PostAsJsonAsync("api/payment-methods", new
        {
            card = new { number = "4111111111111111", expiry = "2030-01", securityCode = "123", name = "Test Shopper", countryCode = "US" },
            alias = "my visa",
        });
        Assert.AreEqual(HttpStatusCode.Created, save.StatusCode);
        using var saveDoc = JsonDocument.Parse(await save.Content.ReadAsStringAsync());
        var pmId = saveDoc.RootElement.GetProperty("paymentMethodId").GetInt32();
        Assert.AreEqual("1111", saveDoc.RootElement.GetProperty("last4").GetString());

        // Pay a new order with the saved card, then fulfil it.
        var orderId = await CreateOrderAsync(shopper, itemId);
        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { savedCardId = pmId });
        pay.EnsureSuccessStatusCode();
        var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", null);
        fulfil.EnsureSuccessStatusCode();

        // Delete the card; it disappears and can no longer be used.
        var del = await shopper.DeleteAsync($"api/payment-methods/{pmId}");
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);

        var order2 = await CreateOrderAsync(shopper, itemId);
        var payDeleted = await shopper.PostAsJsonAsync($"api/orders/{order2}/pay", new { savedCardId = pmId });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, payDeleted.StatusCode);
    }
}
