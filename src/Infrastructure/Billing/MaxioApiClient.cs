using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        EnsureReady();
        // Product family path accepts numeric id or handle prefixed with "handle:" — Maxio Product Families API.
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200&include_archived=false";
        var envelopes = await GetJsonAsync<List<MaxioProductEnvelope>>(path, cancellationToken);
        return envelopes
            .Select(e => e.Product)
            .Where(p => p != null && p.ArchivedAt is null)
            .Cast<MaxioProduct>()
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureReady();
        // GET /customers/lookup.json?reference= — returns a single customer or 404.
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "looking up Maxio customer");
        var envelope = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        EnsureReady();
        var envelope = await PostJsonAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope>(
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            cancellationToken,
            "creating Maxio customer");

        if (envelope.Customer is null)
        {
            throw new BillingException("Maxio did not return a customer after create.", 502);
        }

        return envelope.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        EnsureReady();
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await GetJsonAsync<List<MaxioSubscriptionEnvelope>>(path, cancellationToken);
        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s != null)
            .Cast<MaxioSubscription>()
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        EnsureReady();
        var envelope = await PostJsonAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope>(
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle
                }
            },
            cancellationToken,
            "creating Maxio subscription");

        if (envelope.Subscription is null)
        {
            throw new BillingException("Maxio did not return a subscription after create.", 502);
        }

        return envelope.Subscription;
    }

    private void EnsureReady()
    {
        _options.EnsureConfigured();

        if (_httpClient.BaseAddress is null)
        {
            var baseUrl = _options.TryResolveBaseUrl()
                ?? throw new InvalidOperationException("Maxio API base URL is not configured.");
            _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        }

        if (_httpClient.DefaultRequestHeaders.Authorization is null && !string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            // HTTP Basic: API key as username, literal "x" as password — Maxio Advanced Billing authentication.
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        if (_httpClient.DefaultRequestHeaders.Accept.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, $"GET {path}");
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        CancellationToken cancellationToken,
        string action)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(path, content, cancellationToken);
        await EnsureSuccessAsync(response, action);
        return await DeserializeAsync<TResponse>(response, cancellationToken);
    }

    private async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        if (value is null)
        {
            throw new BillingException("Maxio returned an empty JSON payload.", 502);
        }

        return value;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string action)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var message = TryFormatMaxioError(body) ?? $"Maxio API error while {action} ({(int)response.StatusCode}).";
        _logger.LogWarning("Maxio request failed ({StatusCode}) while {Action}: {Body}", (int)response.StatusCode, action, body);

        var statusCode = response.StatusCode switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => 400,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => 502,
            _ => 502
        };

        throw new BillingException(message, statusCode);
    }

    private static string? TryFormatMaxioError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return null;
            }

            if (errors.ValueKind == JsonValueKind.String)
            {
                return errors.GetString();
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                var parts = errors.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                return string.Join(" ", parts);
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                foreach (var property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            parts.Add($"{property.Name} {item.GetString()}".Trim());
                        }
                    }
                    else
                    {
                        parts.Add($"{property.Name} {property.Value}");
                    }
                }

                return string.Join(" ", parts);
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
