using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class SavedCardTests
{
    [TestMethod]
    public async Task Save_reuse_and_delete_a_card()
    {
        using var factory = new PaymentApiFactory();
        var client = factory.CreateClient();
        client.UseToken(ApiTokenHelper.GetNormalUserToken());

        // Save a card — the response describes it safely, never the full number.
        var saveResponse = await client.PostAsJsonAsync("api/payment-methods", new { card = PaymentApi.VisaCard });
        Assert.AreEqual(HttpStatusCode.Created, saveResponse.StatusCode);
        using var saved = await saveResponse.ReadJsonAsync();
        var paymentMethodId = saved.RootElement.GetProperty("paymentMethodId").GetInt32();
        Assert.AreEqual("VISA", saved.RootElement.GetProperty("brand").GetString());
        Assert.AreEqual("1111", saved.RootElement.GetProperty("lastFourDigits").GetString());
        Assert.IsFalse(saved.RootElement.ToString().Contains("4111111111111111"), "Full card number must never be returned.");

        // It appears in the caller's list.
        using var list = await PaymentApi.GetJsonAsync(client, "api/payment-methods");
        Assert.AreEqual(1, list.RootElement.GetArrayLength());

        // Reuse it to pay a new order.
        var (itemId, _) = await PaymentApi.GetFirstCatalogItemAsync(client);
        var orderId = await PaymentApi.CreateOrderAsync(client, itemId, 1);
        var pay = await PaymentApi.PayWithSavedAsync(client, orderId, paymentMethodId);
        Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode);
        using var payBody = await pay.ReadJsonAsync();
        Assert.AreEqual("Authorized", payBody.RootElement.GetProperty("paymentStatus").GetString());

        // Delete it.
        var delete = await client.DeleteAsync($"api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);

        // Gone from the list, and no longer usable.
        using var listAfter = await PaymentApi.GetJsonAsync(client, "api/payment-methods");
        Assert.AreEqual(0, listAfter.RootElement.GetArrayLength());

        var order2 = await PaymentApi.CreateOrderAsync(client, itemId, 1);
        var payDeleted = await PaymentApi.PayWithSavedAsync(client, order2, paymentMethodId);
        Assert.AreEqual(HttpStatusCode.NotFound, payDeleted.StatusCode);
    }

    [TestMethod]
    public async Task A_saved_card_belongs_only_to_its_owner()
    {
        using var factory = new PaymentApiFactory();
        var owner = factory.CreateClient();
        owner.UseToken(ApiTokenHelper.GetNormalUserToken());
        var other = factory.CreateClient();
        other.UseToken(ApiTokenHelper.GetTokenFor("someoneelse@example.com"));

        var saveResponse = await owner.PostAsJsonAsync("api/payment-methods", new { card = PaymentApi.VisaCard });
        using var saved = await saveResponse.ReadJsonAsync();
        var paymentMethodId = saved.RootElement.GetProperty("paymentMethodId").GetInt32();

        // The other shopper cannot see it or delete it.
        using var otherList = await PaymentApi.GetJsonAsync(other, "api/payment-methods");
        Assert.AreEqual(0, otherList.RootElement.GetArrayLength());

        var otherDelete = await other.DeleteAsync($"api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NotFound, otherDelete.StatusCode);

        // And it is still there for the owner.
        using var ownerList = await PaymentApi.GetJsonAsync(owner, "api/payment-methods");
        Assert.AreEqual(1, ownerList.RootElement.GetArrayLength());
    }
}
