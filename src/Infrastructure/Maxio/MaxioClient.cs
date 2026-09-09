using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for Maxio Advanced Billing, hand-written against
/// maxio-spec/openapi.yaml: Basic auth (API key as username, "x" as password),
/// JSON bodies, snake_case property names, the spec's {site}-templated base URL.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private const int MaxPageSize = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        _logger = logger;
        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Load it from the MAXIO_API_KEY environment variable into user-secrets.");
        }

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(settings.ResolveBaseUrl() + "/");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{settings.ApiKey}:x")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        var payload = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return payload?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("customers.json",
            new MaxioCreateCustomerRequest { Customer = customer }, SerializerOptions, cancellationToken);
        var payload = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return payload!.Customer!;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductFamilyProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        var products = new List<MaxioProduct>();
        var page = 1;

        while (true)
        {
            var response = await _httpClient.GetAsync($"{path}?page={page}&per_page={MaxPageSize}", cancellationToken);
            var payload = await ReadAsync<List<MaxioProductResponse>>(response, cancellationToken);
            if (payload is null || payload.Count == 0)
            {
                break;
            }

            products.AddRange(payload.Where(p => p.Product is not null).Select(p => p.Product!));
            if (payload.Count < MaxPageSize)
            {
                break;
            }
            page++;
        }

        return products;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription }, SerializerOptions, cancellationToken);
        var payload = await ReadAsync<MaxioSubscriptionResponse>(response, cancellationToken);
        return payload!.Subscription!;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        var payload = await ReadAsync<MaxioSubscriptionResponse>(response, cancellationToken);
        return payload?.Subscription;
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(body, SerializerOptions);
        }

        _logger.LogWarning("Maxio API call {Uri} failed with {StatusCode}: {Body}",
            response.RequestMessage?.RequestUri, (int)response.StatusCode, body);

        throw new MaxioApiException((int)response.StatusCode, body, ParseErrors(body));
    }

    /// <summary>
    /// Parses the spec's error models: {"errors": ["msg", ...]},
    /// {"errors": {"field": "msg"}}, {"errors": {"field": ["msg", ...]}}.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string body)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return errors;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return errors;
            }

            switch (errorsElement.ValueKind)
            {
                case JsonValueKind.Array:
                    errors.AddRange(errorsElement.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!));
                    break;
                case JsonValueKind.Object:
                    foreach (var property in errorsElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            errors.Add($"{property.Name}: {property.Value.GetString()}");
                        }
                        else if (property.Value.ValueKind == JsonValueKind.Array)
                        {
                            errors.AddRange(property.Value.EnumerateArray()
                                .Where(e => e.ValueKind == JsonValueKind.String)
                                .Select(e => $"{property.Name}: {e.GetString()}"));
                        }
                    }
                    break;
                case JsonValueKind.String:
                    errors.Add(errorsElement.GetString()!);
                    break;
            }
        }
        catch (JsonException)
        {
            // non-JSON error body (e.g. plain-text 404); surfaced via the exception message
        }

        return errors;
    }
}
