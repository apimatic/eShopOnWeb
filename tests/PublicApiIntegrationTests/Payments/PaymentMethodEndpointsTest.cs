using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class PaymentMethodEndpointsTest : PaymentTestBase
{
    private async Task<int> SaveCardAsync(System.Net.Http.HttpClient client)
    {
        var response = await client.PostAsJsonAsync("api/payment-methods", SaveCardBody());
        response.EnsureSuccessStatusCode();
        return (await ReadJson(response)).GetProperty("paymentMethodId").GetInt32();
    }

    [TestMethod]
    public async Task SaveCard_ReturnsSafeDescriptor_NeverFullNumber()
    {
        var client = AuthedClient(DemoToken);

        var response = await client.PostAsJsonAsync("api/payment-methods", SaveCardBody());

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        StringAssert.DoesNotMatch(raw, new System.Text.RegularExpressions.Regex("4111111111111111"));
        var body = System.Text.Json.JsonDocument.Parse(raw).RootElement;
        Assert.IsTrue(body.GetProperty("paymentMethodId").GetInt32() > 0);
        var pm = body.GetProperty("paymentMethod");
        Assert.AreEqual("VISA", pm.GetProperty("cardBrand").GetString());
        Assert.AreEqual("1111", pm.GetProperty("last4").GetString());
        Assert.AreEqual("2031-11", pm.GetProperty("expiry").GetString());
    }

    [TestMethod]
    public async Task SaveCard_Unauthenticated_Returns401()
    {
        var response = await AnonymousClient().PostAsJsonAsync("api/payment-methods", SaveCardBody());
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SaveCard_MissingNumber_Returns400()
    {
        var client = AuthedClient(DemoToken);
        var response = await client.PostAsJsonAsync("api/payment-methods", new
        {
            card = new { cardholderName = "T", number = "", expiryMonth = 1, expiryYear = 2030, securityCode = "123", billingAddress = new { addressLine1 = "1", city = "SF", postalCode = "1", countryCode = "US" } }
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ListCards_ReturnsCallersSavedCards()
    {
        var client = AuthedClient(DemoToken);
        await SaveCardAsync(client);

        var body = await ReadJson(await client.GetAsync("api/payment-methods"));

        Assert.AreEqual(1, body.GetProperty("paymentMethods").GetArrayLength());
    }

    [TestMethod]
    public async Task PayWithSavedCard_MarksOrderPaid()
    {
        var client = AuthedClient(DemoToken);
        var paymentMethodId = await SaveCardAsync(client);
        var orderId = await CreateOrderAsync(client);

        var response = await client.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("Paid", (await ReadJson(response)).GetProperty("order").GetProperty("paymentStatus").GetString());
        Assert.AreEqual(1, Factory.Gateway.ChargeSavedCardCalls);
    }

    [TestMethod]
    public async Task DeleteCard_ThenNotListed_AndNotUsable()
    {
        var client = AuthedClient(DemoToken);
        var paymentMethodId = await SaveCardAsync(client);

        var delete = await client.DeleteAsync($"api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);

        var list = await ReadJson(await client.GetAsync("api/payment-methods"));
        Assert.AreEqual(0, list.GetProperty("paymentMethods").GetArrayLength());

        var orderId = await CreateOrderAsync(client);
        var pay = await client.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId });
        Assert.AreEqual(HttpStatusCode.NotFound, pay.StatusCode);
    }

    [TestMethod]
    public async Task SavedCard_IsPrivateToOwner()
    {
        var owner = AuthedClient(DemoToken);
        var paymentMethodId = await SaveCardAsync(owner);

        var other = AuthedClient(OtherToken);

        // cannot see
        var otherList = await ReadJson(await other.GetAsync("api/payment-methods"));
        Assert.AreEqual(0, otherList.GetProperty("paymentMethods").GetArrayLength());

        // cannot delete
        var otherDelete = await other.DeleteAsync($"api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NotFound, otherDelete.StatusCode);

        // cannot use to pay their own order
        var otherOrderId = await CreateOrderAsync(other);
        var otherPay = await other.PostAsJsonAsync($"api/orders/{otherOrderId}/pay", new { paymentMethodId });
        Assert.AreEqual(HttpStatusCode.NotFound, otherPay.StatusCode);

        // owner still has it
        var ownerList = await ReadJson(await owner.GetAsync("api/payment-methods"));
        Assert.AreEqual(1, ownerList.GetProperty("paymentMethods").GetArrayLength());
    }
}
