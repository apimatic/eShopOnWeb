using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.PaymentEndpoints;

internal static class PaymentTestHelpers
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // PayPal sandbox test card.
    public const string TestCardNumber = "4111111111111111";
    public const string TestCardExpiry = "2030-01";
    public const string TestCardCvc = "123";

    public static HttpClient ClientFor(PaymentApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static StringContent JsonBody(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    public static object OneOffCard() => new
    {
        number = TestCardNumber,
        expiry = TestCardExpiry,
        securityCode = TestCardCvc,
        cardholderName = "Test Shopper"
    };

    public static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(body, Json);
    }

    public static async Task<int> CreateOrderAsync(HttpClient client, int catalogItemId = 1, int quantity = 2)
    {
        var payload = new { items = new[] { new { catalogItemId, quantity } } };
        var response = await client.PostAsync("api/orders", JsonBody(payload));
        response.EnsureSuccessStatusCode();
        var json = await ReadJson(response);
        return json.GetProperty("orderId").GetInt32();
    }
}
