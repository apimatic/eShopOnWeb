using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentMethodEndpoints;

[TestClass]
public class PaymentMethodEndpointsTest
{
    private static HttpClient AuthenticatedClient(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent CardBody() => new StringContent(JsonSerializer.Serialize(new
    {
        card = new
        {
            name = "Jane Roe",
            number = "4111111111111111",
            expiry = "2031-06",
            securityCode = "456",
            addressLine1 = "1 Test St",
            city = "Testville",
            postalCode = "98000",
            countryCode = "US"
        }
    }), Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task Create_RequiresAuthentication()
    {
        var client = ProgramTest.NewClient;
        var response = await client.PostAsync("api/payment-methods", CardBody());
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Vaults a real card with the PayPal sandbox and confirms only a safe descriptor comes back.</summary>
    [TestMethod]
    public async Task Create_VaultsRealCard_AndReturnsSafeDescriptorOnly()
    {
        var client = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/payment-methods", CardBody());
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadAsStringAsync()).FromJson<CreatePaymentMethodResponse>();

        Assert.IsTrue(result!.PaymentMethodId > 0);
        Assert.AreEqual("VISA", result.PaymentMethod.CardBrand);
        Assert.AreEqual("1111", result.PaymentMethod.LastDigits);
        var raw = await response.Content.ReadAsStringAsync();
        StringAssert.DoesNotMatch(raw, new System.Text.RegularExpressions.Regex("4111111111111111"));
    }

    [TestMethod]
    public async Task List_OnlyReturnsCallersOwnSavedCards()
    {
        var owner = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var createResponse = await owner.PostAsync("api/payment-methods", CardBody());
        createResponse.EnsureSuccessStatusCode();
        var created = (await createResponse.Content.ReadAsStringAsync()).FromJson<CreatePaymentMethodResponse>();

        var ownerList = (await (await owner.GetAsync("api/payment-methods")).Content.ReadAsStringAsync()).FromJson<ListPaymentMethodsResponse>();
        Assert.IsTrue(ownerList!.PaymentMethods.Any(p => p.PaymentMethodId == created!.PaymentMethodId));

        var otherBuyer = AuthenticatedClient(ApiTokenHelper.GetOtherUserToken());
        var otherList = (await (await otherBuyer.GetAsync("api/payment-methods")).Content.ReadAsStringAsync()).FromJson<ListPaymentMethodsResponse>();
        Assert.IsFalse(otherList!.PaymentMethods.Any(p => p.PaymentMethodId == created!.PaymentMethodId));
    }

    [TestMethod]
    public async Task Delete_ReturnsNotFound_ForAnotherBuyersCard()
    {
        var owner = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var createResponse = await owner.PostAsync("api/payment-methods", CardBody());
        var created = (await createResponse.Content.ReadAsStringAsync()).FromJson<CreatePaymentMethodResponse>();

        var otherBuyer = AuthenticatedClient(ApiTokenHelper.GetOtherUserToken());
        var deleteResponse = await otherBuyer.DeleteAsync($"api/payment-methods/{created!.PaymentMethodId}");

        Assert.AreEqual(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }

    /// <summary>Deletes a real vaulted card and confirms it is gone both from our list and unusable to pay.</summary>
    [TestMethod]
    public async Task Delete_RemovesCard_AndItNoLongerAppearsOrPays()
    {
        var buyer = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var createResponse = await buyer.PostAsync("api/payment-methods", CardBody());
        var created = (await createResponse.Content.ReadAsStringAsync()).FromJson<CreatePaymentMethodResponse>();

        var deleteResponse = await buyer.DeleteAsync($"api/payment-methods/{created!.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);

        var list = (await (await buyer.GetAsync("api/payment-methods")).Content.ReadAsStringAsync()).FromJson<ListPaymentMethodsResponse>();
        Assert.IsFalse(list!.PaymentMethods.Any(p => p.PaymentMethodId == created.PaymentMethodId));

        var orderBody = new StringContent(JsonSerializer.Serialize(new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } },
            shipToAddress = new { street = "1 Test St", city = "Testville", state = "WA", country = "US", zipCode = "98000" }
        }), Encoding.UTF8, "application/json");
        var orderResponse = await buyer.PostAsync("api/orders", orderBody);
        var order = (await orderResponse.Content.ReadAsStringAsync()).FromJson<Microsoft.eShopWeb.PublicApi.OrderEndpoints.CreateOrderResponse>();

        var payBody = new StringContent(JsonSerializer.Serialize(new { paymentMethodId = created.PaymentMethodId }), Encoding.UTF8, "application/json");
        var payResponse = await buyer.PostAsync($"api/orders/{order!.OrderId}/pay", payBody);

        Assert.AreEqual(HttpStatusCode.BadRequest, payResponse.StatusCode);
    }
}
