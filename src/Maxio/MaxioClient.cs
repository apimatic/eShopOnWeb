using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Configuration;
using Maxio.Exceptions;
using Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing API, built against the Maxio OpenAPI specification.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Customer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildExceptionAsync(response, body, cancellationToken);
        }

        var result = await response.Content.ReadFromJsonAsync<CustomerResponse>(JsonOptions, cancellationToken);
        return result?.Customer;
    }

    public async Task<Customer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var request = new CreateCustomerRequest { Customer = customer };
        using var response = await _httpClient.PostAsJsonAsync("/customers.json", request, JsonOptions, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildExceptionAsync(response, body, cancellationToken);
        }

        var result = await response.Content.ReadFromJsonAsync<CustomerResponse>(JsonOptions, cancellationToken);
        return result?.Customer ?? throw new MaxioApiException(response.StatusCode, body, Array.Empty<string>(), "Maxio returned an empty customer response.");
    }

    public async Task<IReadOnlyList<Product>> ListProductsForProductFamilyAsync(string productFamilyIdOrHandle, CancellationToken cancellationToken = default)
    {
        var url = $"/product_families/{Uri.EscapeDataString(productFamilyIdOrHandle)}/products.json";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildExceptionAsync(response, body, cancellationToken);
        }

        var results = await response.Content.ReadFromJsonAsync<List<ProductResponse>>(JsonOptions, cancellationToken);
        return results?.Select(r => r.Product).ToList() ?? new List<Product>();
    }

    public async Task<Subscription> CreateSubscriptionAsync(CreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequest { Subscription = subscription };
        using var response = await _httpClient.PostAsJsonAsync("/subscriptions.json", request, JsonOptions, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildExceptionAsync(response, body, cancellationToken);
        }

        var result = await response.Content.ReadFromJsonAsync<SubscriptionResponse>(JsonOptions, cancellationToken);
        return result?.Subscription ?? throw new MaxioApiException(response.StatusCode, body, Array.Empty<string>(), "Maxio returned an empty subscription response.");
    }

    public async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var url = $"/customers/{customerId}/subscriptions.json";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildExceptionAsync(response, body, cancellationToken);
        }

        var results = await response.Content.ReadFromJsonAsync<List<SubscriptionResponse>>(JsonOptions, cancellationToken);
        return results?.Select(r => r.Subscription).ToList() ?? new List<Subscription>();
    }

    /// <summary>
    /// Resolves the Maxio API base address. Uses <see cref="MaxioOptions.BaseUrl"/> verbatim when set,
    /// otherwise derives it from the site subdomain using the default server template from the spec
    /// (https://{site}.chargify.com).
    /// </summary>
    public static string ResolveBaseUrl(MaxioOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is not configured. Set MAXIO_SITE_SUBDOMAIN or Maxio:BaseUrl.");
        }

        return $"https://{options.Subdomain}.chargify.com";
    }

    private async Task<MaxioApiException> BuildExceptionAsync(HttpResponseMessage response, string body, CancellationToken cancellationToken)
    {
        var errors = ExtractErrors(body);
        var message = errors.Count > 0
            ? $"Maxio API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}): {string.Join(" ", errors)}"
            : $"Maxio API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).";

        _logger.LogError("Maxio API request to {Method} {Url} failed with status {StatusCode}. Response: {Body}",
            response.RequestMessage?.Method, response.RequestMessage?.RequestUri, (int)response.StatusCode, body);

        return new MaxioApiException(response.StatusCode, body, errors, message);
    }

    /// <summary>
    /// Extracts human-readable error messages from a Maxio error response body. The spec models the
    /// "errors" property as a string, an array of strings, or a map of attribute to message.
    /// </summary>
    private static IReadOnlyList<string> ExtractErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return Array.Empty<string>();
            }

            var errors = new List<string>();
            switch (errorsElement.ValueKind)
            {
                case JsonValueKind.String:
                    errors.Add(errorsElement.GetString() ?? string.Empty);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in errorsElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            errors.Add(item.GetString() ?? string.Empty);
                        }
                        else
                        {
                            errors.Add(item.GetRawText());
                        }
                    }
                    break;
                case JsonValueKind.Object:
                    foreach (var property in errorsElement.EnumerateObject())
                    {
                        errors.Add($"{property.Name}: {property.Value.GetRawText()}");
                    }
                    break;
            }

            return errors;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
