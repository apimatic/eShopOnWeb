using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByEmailAsync(string email, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
}

public sealed record MaxioCreateCustomer(string FirstName, string LastName, string Email, string Reference);

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private readonly HttpClient _httpClient;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new MaxioConfigurationException("Maxio:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.GetBaseUrl()))
        {
            throw new MaxioConfigurationException("Maxio:BaseUrl could not be resolved.");
        }

        _httpClient = httpClient;
        var baseUrl = settings.GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString(productFamilyHandle);
        using var response = await _httpClient.GetAsync(
            $"product_families/handle:{family}/products.json?include_archived=false&per_page=200",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var products = await response.Content.ReadFromJsonAsync<List<MaxioProductResponse>>(cancellationToken: cancellationToken);
        return products?.Where(x => x.Product is not null).Select(x => x.Product).ToArray()
            ?? Array.Empty<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(cancellationToken: cancellationToken);
        return result?.Customer;
    }

    public async Task<MaxioCustomer?> FindCustomerByEmailAsync(string email, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"customers.json?q={Uri.EscapeDataString(email)}&per_page=50",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var results = await response.Content.ReadFromJsonAsync<List<MaxioCustomerResponse>>(cancellationToken: cancellationToken);
        return results?
            .Select(x => x.Customer)
            .Where(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Id)
            .FirstOrDefault();
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        var request = new
        {
            customer = new
            {
                first_name = customer.FirstName,
                last_name = customer.LastName,
                email = customer.Email,
                reference = customer.Reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(cancellationToken: cancellationToken);
        return result?.Customer ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty customer response.");
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(cancellationToken: cancellationToken);
        return result?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference, CancellationToken cancellationToken)
    {
        var request = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle,
                payment_collection_method = "remittance",
                reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(cancellationToken: cancellationToken);
        return result?.Subscription ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty subscription response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var subscriptions = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionResponse>>(cancellationToken: cancellationToken);
        return subscriptions?.Where(x => x.Subscription is not null).Select(x => x.Subscription).ToArray()
            ?? Array.Empty<MaxioSubscription>();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await MaxioErrorMessageAsync(response, cancellationToken);
        throw new MaxioApiException((int)response.StatusCode, message);
    }

    private static async Task<string> MaxioErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"Maxio returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    return string.Join(" ", errors.EnumerateArray().Select(x => x.ToString()));
                }

                if (errors.ValueKind == JsonValueKind.Object)
                {
                    return string.Join(" ", errors.EnumerateObject().Select(x => $"{x.Name}: {x.Value}"));
                }
            }
        }
        catch (JsonException)
        {
            // Preserve a useful status-based message for non-JSON error bodies.
        }

        return $"Maxio returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
    }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
