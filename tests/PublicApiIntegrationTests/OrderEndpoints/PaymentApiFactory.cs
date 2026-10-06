using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Payments.Adyen;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.OrderEndpoints;

/// <summary>
/// The real PublicApi host with Adyen replaced at the HttpClient seam by <see cref="StubAdyenHandler"/>.
/// </summary>
public sealed class PaymentApiFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "offline-test-api-key";
    public const string MerchantAccount = "OfflineTestMerchant";

    private readonly IDictionary<string, string?> _settings;

    public PaymentApiFactory(IDictionary<string, string?>? settings = null)
    {
        _settings = settings ?? new Dictionary<string, string?>();
    }

    public StubAdyenHandler Adyen { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Adyen:ApiKey", ApiKey);
        builder.UseSetting("Adyen:MerchantAccount", MerchantAccount);
        builder.UseSetting("Adyen:Environment", "test");
        builder.UseSetting("Adyen:Currency", "USD");
        // Added after the app's own sources (including appsettings.test.json), so these win.
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(AdyenServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Adyen);
        });
    }

    public HttpClient ClientFor(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

public static class PaymentApiClientExtensions
{
    public static readonly object TestCard = new
    {
        encryptedCardNumber = "test_4111111145551142",
        encryptedExpiryMonth = "test_03",
        encryptedExpiryYear = "test_2030",
        encryptedSecurityCode = "test_737",
        holderName = "Test Shopper"
    };

    public static async Task<int> PlaceOrderAsync(this HttpClient client, params (int CatalogItemId, int Quantity)[] lines)
    {
        var response = await client.PostAsJsonAsync("api/orders", new
        {
            items = System.Linq.Enumerable.Select(lines, l => new { catalogItemId = l.CatalogItemId, quantity = l.Quantity })
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return json.GetProperty("orderId").GetInt32();
    }

    public static Task<HttpResponseMessage> PayAsync(this HttpClient client, int orderId, object? card = null) =>
        client.PostAsJsonAsync($"api/orders/{orderId}/pay", card ?? TestCard);
}
