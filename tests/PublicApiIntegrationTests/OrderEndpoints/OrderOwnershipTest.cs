using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderEndpoints;

/// <summary>One shopper must never be able to act on another shopper's order. These checks
/// happen before any PayPal call is made, so they're safe to verify without live credentials.</summary>
[TestClass]
public class OrderOwnershipTest
{
    [TestMethod]
    public async Task PayingAnotherShoppersOrderIsForbidden()
    {
        var client = ProgramTest.NewClient;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var placeRequest = new { Items = new[] { new { CatalogItemId = 3, Quantity = 1 } } };
        var placeContent = new StringContent(JsonSerializer.Serialize(placeRequest), Encoding.UTF8, "application/json");
        var placeResponse = await client.PostAsync("api/orders", placeContent);
        placeResponse.EnsureSuccessStatusCode();
        var placed = (await placeResponse.Content.ReadAsStringAsync()).FromJson<PlaceOrderResponse>()!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        var payRequest = new
        {
            Card = new
            {
                Number = "4111111111111111",
                Expiry = "2030-01",
                SecurityCode = "123",
                CardholderName = "Someone Else",
                AddressLine1 = "1 Test St",
                City = "Testville",
                PostalCode = "12345",
                CountryCode = "US"
            }
        };
        var payContent = new StringContent(JsonSerializer.Serialize(payRequest), Encoding.UTF8, "application/json");
        var payResponse = await client.PostAsync($"api/orders/{placed.OrderId}/pay", payContent);

        Assert.AreEqual(HttpStatusCode.Forbidden, payResponse.StatusCode);
    }

    [TestMethod]
    public async Task PayingUnknownOrderReturnsNotFound()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var payRequest = new { PaymentMethodId = 12345 };
        var payContent = new StringContent(JsonSerializer.Serialize(payRequest), Encoding.UTF8, "application/json");
        var payResponse = await client.PostAsync("api/orders/999999/pay", payContent);

        Assert.AreEqual(HttpStatusCode.NotFound, payResponse.StatusCode);
    }
}
