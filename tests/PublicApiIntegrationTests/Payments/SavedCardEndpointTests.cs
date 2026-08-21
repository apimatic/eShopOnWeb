using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class SavedCardEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private PaymentApiFactory _factory = null!;

    private static object VisaCard => new
    {
        number = "4111111111111111",
        expiry = "2030-01",
        securityCode = "123",
        cardholderName = "Test Buyer"
    };

    [TestInitialize]
    public void Init() => _factory = new PaymentApiFactory();

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Body(object o) =>
        new(JsonSerializer.Serialize(o, Json), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> DocAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [TestMethod]
    public async Task Save_List_PayWith_Delete_SavedCard_Flow()
    {
        var shopper = ClientFor(TestTokens.ForShopper("cardholder@test.com"));

        // Save a card — response identifies and safely describes it, never the full number.
        var saveResponse = await shopper.PostAsync("api/payment-methods",
            Body(new { card = VisaCard, alias = "My Visa" }));
        Assert.AreEqual(HttpStatusCode.Created, saveResponse.StatusCode);
        var rawSave = await saveResponse.Content.ReadAsStringAsync();
        Assert.IsFalse(rawSave.Contains("4111111111111111"), "Full card number must never be returned.");
        var saved = JsonDocument.Parse(rawSave).RootElement;
        var paymentMethodId = saved.GetProperty("paymentMethodId").GetInt32();
        Assert.IsTrue(paymentMethodId > 0);
        Assert.AreEqual("1111", saved.GetProperty("last4").GetString());
        Assert.AreEqual("VISA", saved.GetProperty("brand").GetString());

        // List shows the saved card.
        var list = await DocAsync(await shopper.GetAsync("api/payment-methods"));
        Assert.IsTrue(list.EnumerateArray().Any(c => c.GetProperty("id").GetInt32() == paymentMethodId));

        // Pay a new order using the saved card.
        var orderId = (await DocAsync(await shopper.PostAsync("api/orders",
            Body(new { items = new[] { new { catalogItemId = 2, quantity = 1 } } })))).GetProperty("orderId").GetInt32();
        var pay = await shopper.PostAsync($"api/orders/{orderId}/pay", Body(new { savedPaymentMethodId = paymentMethodId }));
        Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode);
        Assert.AreEqual("Authorized", (await DocAsync(pay)).GetProperty("status").GetString());

        // Delete it.
        var delete = await shopper.DeleteAsync($"api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.IsFalse(_factory.Gateway.DeletedVaultIds.IsEmpty, "The vaulted card should be removed from PayPal too.");

        // It no longer appears and can no longer be used to pay.
        var listAfter = await DocAsync(await shopper.GetAsync("api/payment-methods"));
        Assert.IsFalse(listAfter.EnumerateArray().Any(c => c.GetProperty("id").GetInt32() == paymentMethodId));

        var order2 = (await DocAsync(await shopper.PostAsync("api/orders",
            Body(new { items = new[] { new { catalogItemId = 2, quantity = 1 } } })))).GetProperty("orderId").GetInt32();
        var payDeleted = await shopper.PostAsync($"api/orders/{order2}/pay", Body(new { savedPaymentMethodId = paymentMethodId }));
        Assert.AreEqual(HttpStatusCode.NotFound, payDeleted.StatusCode);
    }

    [TestMethod]
    public async Task SavedCards_AreScopedToTheOwningShopper()
    {
        var alice = ClientFor(TestTokens.ForShopper("alice-cards@test.com"));
        var bob = ClientFor(TestTokens.ForShopper("bob-cards@test.com"));

        var aliceCardId = (await DocAsync(await alice.PostAsync("api/payment-methods",
            Body(new { card = VisaCard, alias = "Alice Visa" })))).GetProperty("paymentMethodId").GetInt32();

        // Bob does not see Alice's card.
        var bobList = await DocAsync(await bob.GetAsync("api/payment-methods"));
        Assert.AreEqual(0, bobList.EnumerateArray().Count());

        // Bob cannot delete Alice's card.
        var bobDelete = await bob.DeleteAsync($"api/payment-methods/{aliceCardId}");
        Assert.AreEqual(HttpStatusCode.NotFound, bobDelete.StatusCode);
    }
}
