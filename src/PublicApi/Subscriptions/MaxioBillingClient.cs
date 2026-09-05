using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken);
    Task<MaxioProduct?> GetPlanAsync(string productHandle, CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomer customer, CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscription subscription, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string? details = null)
        : base($"Maxio Billing API request failed with status {(int)statusCode}.")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress = BuildBaseAddress(_options);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:X")));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var plans = new List<MaxioProduct>();
        for (var page = 1; ; page++)
        {
            var path = $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json?page={page}&per_page=200";
            var response = await GetRequiredAsync<List<MaxioProductListItem>>(path, cancellationToken);
            plans.AddRange(response.ConvertAll(item => item.Product));
            if (response.Count < 200) return plans;
        }
    }

    public async Task<MaxioProduct?> GetPlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var response = await GetOrDefaultAsync<MaxioProductResponse>($"products/handle/{Uri.EscapeDataString(productHandle)}.json", cancellationToken);
        if (response is null) return null;
        return IsConfiguredFamily(response.Product) ? response.Product : null;
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await GetOrDefaultAsync<MaxioCustomerResponse>($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomer customer, CancellationToken cancellationToken)
    {
        var response = await PostRequiredAsync<MaxioCustomerResponse>("customers.json", new
        {
            customer = new
            {
                first_name = customer.FirstName,
                last_name = customer.LastName,
                email = customer.Email,
                reference = customer.Reference
            }
        }, cancellationToken);
        return response.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await GetOrDefaultAsync<MaxioSubscriptionResponse>($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscription subscription, CancellationToken cancellationToken)
    {
        var response = await PostRequiredAsync<MaxioSubscriptionResponse>("subscriptions.json", new
        {
            subscription = new
            {
                product_handle = subscription.ProductHandle,
                customer_id = subscription.CustomerId,
                reference = subscription.Reference,
                next_billing_at = subscription.NextBillingAt
            }
        }, cancellationToken);
        return response.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var subscriptions = new List<MaxioSubscription>();
        for (var page = 1; ; page++)
        {
            var response = await GetRequiredAsync<List<MaxioSubscriptionListItem>>($"customers/{customerId}/subscriptions.json?page={page}&per_page=200", cancellationToken);
            subscriptions.AddRange(response.ConvertAll(item => item.Subscription));
            if (response.Count < 200) return subscriptions;
        }
    }

    private bool IsConfiguredFamily(MaxioProduct product) =>
        product.ArchivedAt is null &&
        string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal);

    private async Task<T?> GetOrDefaultAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken)
            ?? throw new MaxioApiException(response.StatusCode);
    }

    private async Task<T> GetRequiredAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        var value = await GetOrDefaultAsync<T>(path, cancellationToken);
        return value ?? throw new MaxioApiException(HttpStatusCode.NotFound);
    }

    private async Task<T> PostRequiredAsync<T>(string path, object body, CancellationToken cancellationToken) where T : class
    {
        using var response = await _httpClient.PostAsJsonAsync(path, body, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken)
            ?? throw new MaxioApiException(response.StatusCode);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var details = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Maxio Billing API returned HTTP {StatusCode}: {Details}", (int)response.StatusCode, details);
            throw new MaxioApiException(response.StatusCode, details);
        }
    }

    private static Uri BuildBaseAddress(MaxioOptions options)
    {
        var address = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? $"https://{options.Subdomain}.chargify.com/"
            : options.BaseUrl;

        if (!Uri.TryCreate(address, UriKind.Absolute, out var baseAddress) || baseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new OptionsValidationException(MaxioOptions.SectionName, typeof(MaxioOptions), new[] { "Maxio:BaseUrl must be an absolute HTTPS URL." });
        }

        return baseAddress.ToString().EndsWith("/", StringComparison.Ordinal) ? baseAddress : new Uri(baseAddress + "/");
    }
}
