using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentMethodEndpoints;

[TestClass]
public class SavedCardFlowTests
{
    private static PaymentsApiFactory _factory = new();

    [ClassInitialize]
    public static void ClassInitialize(TestContext _) => _factory = new PaymentsApiFactory();

    private static HttpClient AuthorizedClient(string token)
    {
        var client = _factory.NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object ValidCard() => new
    {
        number = "4111111111111111",
        expiryMonth = 9,
        expiryYear = 2031,
        securityCode = "456",
        cardholderName = "Jane Doe",
        billingAddress = new { street = "123 Main St", city = "Kent", state = "OH", country = "US", zipCode = "44240" }
    };

    [TestMethod]
    public async Task SaveListAndDelete_RemovesTheCardFromTheListAndMakesItUnusable()
    {
        var user = AuthorizedClient(ApiTokenHelper.GetNormalUserToken());

        var saveResponse = await user.PostAsJsonAsync("api/payment-methods", ValidCard());
        Assert.AreEqual(HttpStatusCode.Created, saveResponse.StatusCode);
        var saved = JsonDocument.Parse(await saveResponse.Content.ReadAsStringAsync()).RootElement;
        var paymentMethodId = saved.GetProperty("paymentMethodId").GetInt32();
        Assert.IsTrue(paymentMethodId > 0);
        // Never the full card number, only safe-to-display fields.
        Assert.AreEqual("1111", saved.GetProperty("paymentMethod").GetProperty("last4").GetString());

        var listResponse = await user.GetAsync("api/payment-methods");
        var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("paymentMethods");
        Assert.AreEqual(1, list.GetArrayLength());

        var deleteResponse = await user.DeleteAsync($"api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);

        var listAfterDelete = await user.GetAsync("api/payment-methods");
        var listAfter = JsonDocument.Parse(await listAfterDelete.Content.ReadAsStringAsync()).RootElement.GetProperty("paymentMethods");
        Assert.AreEqual(0, listAfter.GetArrayLength());

        var placeResponse = await user.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });
        var orderId = JsonDocument.Parse(await placeResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("orderId").GetInt32();
        var payWithDeletedCard = await user.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId });
        Assert.AreEqual(HttpStatusCode.NotFound, payWithDeletedCard.StatusCode);
    }

    [TestMethod]
    public async Task OneShopperCannotSeeAnothersSavedCards()
    {
        var owner = AuthorizedClient(ApiTokenHelper.GetNormalUserToken());
        await owner.PostAsJsonAsync("api/payment-methods", ValidCard());

        var otherShopper = AuthorizedClient(ApiTokenHelper.GetAdminUserToken());
        var listResponse = await otherShopper.GetAsync("api/payment-methods");
        var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("paymentMethods");

        Assert.AreEqual(0, list.GetArrayLength());
    }
}
