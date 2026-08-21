using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>Small HTTP/JSON helpers shared by the payment functional tests.</summary>
internal static class PaymentApi
{
    public static readonly object VisaCard = new
    {
        number = "4111111111111111",
        expiry = "2030-01",
        securityCode = "123",
        cardholderName = "Test Buyer",
        billingLine1 = "1 Market St",
        billingCity = "San Jose",
        billingState = "CA",
        billingPostalCode = "95131",
        countryCode = "US"
    };

    public static void UseToken(this HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public static async Task<(int id, decimal price)> GetFirstCatalogItemAsync(HttpClient client)
    {
        var doc = await GetJsonAsync(client, "api/catalog-items?pageSize=1&pageIndex=0");
        var item = doc.RootElement.GetProperty("catalogItems")[0];
        return (item.GetProperty("id").GetInt32(), item.GetProperty("price").GetDecimal());
    }

    public static async Task<int> CreateOrderAsync(HttpClient client, int catalogItemId, int quantity)
    {
        var response = await client.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId, quantity } }
        });
        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("orderId").GetInt32();
    }

    public static Task<HttpResponseMessage> PayWithCardAsync(HttpClient client, int orderId) =>
        client.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card = VisaCard });

    public static Task<HttpResponseMessage> PayWithSavedAsync(HttpClient client, int orderId, int savedPaymentMethodId) =>
        client.PostAsJsonAsync($"api/orders/{orderId}/pay", new { savedPaymentMethodId });

    public static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    public static async Task<JsonDocument> ReadJsonAsync(this HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
