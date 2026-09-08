using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Direct access to the Maxio sandbox used only to clean up subscriptions/customers
/// that the public API tests create. These tests require the MAXIO_* environment
/// variables; they are skipped (inconclusive) when they are absent.
/// </summary>
public static class MaxioTestContext
{
    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAXIO_API_KEY")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY"));

    private static string ApiKey => Environment.GetEnvironmentVariable("MAXIO_API_KEY")!;

    private static string Subdomain => Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN")!;

    public static string BaseUrl
    {
        get
        {
            var overrideUrl = Environment.GetEnvironmentVariable("MAXIO_BASE_URL");
            return string.IsNullOrWhiteSpace(overrideUrl)
                ? $"https://{Subdomain}.chargify.com"
                : overrideUrl.TrimEnd('/');
        }
    }

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(60) };
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{ApiKey}:x"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>Cancels a Maxio subscription (DELETE /subscriptions/{id}.json).</summary>
    public static async Task CancelSubscriptionAsync(long subscriptionId)
    {
        try
        {
            using var response = await Http.DeleteAsync($"subscriptions/{subscriptionId}.json");
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException)
        {
            // Best-effort cleanup only.
        }
    }

    /// <summary>Deletes the Maxio customer carrying the given reference, when one exists and is deletable.</summary>
    public static async Task DeleteCustomerByReferenceAsync(string reference)
    {
        try
        {
            using var lookup = await Http.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
            if (!lookup.IsSuccessStatusCode)
            {
                return;
            }

            var content = await lookup.Content.ReadAsStringAsync();
            var customerId = System.Text.Json.JsonDocument.Parse(content).RootElement.GetProperty("customer").GetProperty("id").GetInt64();
            using var delete = await Http.DeleteAsync($"customers/{customerId}.json");
            if (!delete.IsSuccessStatusCode)
            {
                // 422 when the customer still owns subscriptions; leave it in place.
            }
        }
        catch (HttpRequestException)
        {
            // Best-effort cleanup only.
        }
    }
}
