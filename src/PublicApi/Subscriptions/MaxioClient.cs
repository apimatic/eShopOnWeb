using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Small typed client for the subset of the checked-in Maxio OpenAPI contract used by subscriptions.
/// </summary>
public sealed class MaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // Maxio's OpenAPI schemas use snake_case wire names (for example, price_in_cents).
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? BuildMaxioBaseUrl(_options.Subdomain)
            : _options.BaseUrl;

        _httpClient.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        // GET /product_families/{product_family_id}/products.json accepts "handle:{handle}" per the spec.
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        using var response = await _httpClient.GetAsync($"product_families/{family}/products.json", cancellationToken);
        return (await ReadAsync<List<MaxioProductResponse>>(response, cancellationToken)).Select(x => x.Product).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        // GET /customers/lookup.json?reference= is the exact-reference lookup operation in the spec.
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        return (await ReadAsync<MaxioCustomerResponse>(response, cancellationToken)).Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken)
    {
        // POST /customers.json, Create-Customer-Request in the spec.
        var payload = new
        {
            customer = new { first_name = firstName, last_name = lastName, email, reference }
        };
        using var response = await _httpClient.PostAsJsonAsync("customers.json", payload, JsonOptions, cancellationToken);
        return (await ReadAsync<MaxioCustomerResponse>(response, cancellationToken)).Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        // GET /customers/{customer_id}/subscriptions.json from the customer operations in the spec.
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        return (await ReadAsync<List<MaxioSubscriptionResponse>>(response, cancellationToken)).Select(x => x.Subscription).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        // POST /subscriptions.json, Create-Subscription-Request in the spec.
        // The demo plans do not require a payment profile. "remittance" is a Collection-Method
        // enum value in the Maxio contract and prevents automatic card capture at signup.
        var payload = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle,
                payment_collection_method = "remittance"
            }
        };
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, JsonOptions, cancellationToken);
        return (await ReadAsync<MaxioSubscriptionResponse>(response, cancellationToken)).Subscription;
    }

    private static string BuildMaxioBaseUrl(string subdomain)
    {
        // The OpenAPI server template has US chargify.com and EU ebilling.maxio.com production hosts.
        // Sandbox site credentials use the same site-template host; MAXIO_ENVIRONMENT selects its region.
        var environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
        var host = string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"https://{subdomain}.ebilling.maxio.com"
            : $"https://{subdomain}.chargify.com";
        return host;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new MaxioApiException(response.StatusCode, body);

        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty JSON response.");
    }
}
