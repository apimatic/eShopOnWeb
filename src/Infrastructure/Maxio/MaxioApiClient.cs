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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing JSON API. All requests use Basic auth
/// with the site API key. 404s are surfaced as null results; other non-success
/// statuses raise <see cref="MaxioApiException"/>.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly IAppLogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IAppLogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioProductFamily?> FindProductFamilyAsync(string handle, CancellationToken cancellationToken)
    {
        var families = await GetListAsync<MaxioProductFamilyEnvelope>("/product_families.json", cancellationToken);
        return families.Select(e => e.ProductFamily)
            .FirstOrDefault(f => string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(int productFamilyId, CancellationToken cancellationToken)
    {
        var envelopes = await GetListAsync<MaxioProductEnvelope>($"/product_families/{productFamilyId}/products.json", cancellationToken);
        return envelopes.Select(e => e.Product).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        if (await GetSingleAsync<MaxioCustomerEnvelope>($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
                cancellationToken) is not { } envelope)
        {
            return null;
        }
        return envelope.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference,
            }
        };
        var response = await SendAsync(HttpMethod.Post, "/customers.json", payload, cancellationToken);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        var customer = envelope?.Customer;
        return customer ?? throw new MaxioApiException("Maxio returned an empty customer payload.",
            (int)response.StatusCode, Array.Empty<string>());
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId,
        CancellationToken cancellationToken)
    {
        var envelopes = await GetListAsync<MaxioSubscriptionEnvelope>($"/customers/{customerId}/subscriptions.json", cancellationToken);
        return envelopes.Select(e => e.Subscription).ToList();
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        if (await GetSingleAsync<MaxioSubscriptionEnvelope>($"/subscriptions/{subscriptionId}.json", cancellationToken)
            is not { } envelope)
        {
            return null;
        }
        return envelope.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle,
                // The seeded plans do not require a card; remittance (external payment)
                // is the verified way to enroll without storing a payment method.
                payment_collection_method = "remittance",
            }
        };
        var response = await SendAsync(HttpMethod.Post, "/subscriptions.json", payload, cancellationToken);
        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription ?? throw new MaxioApiException("Maxio returned an empty subscription payload.",
            (int)response.StatusCode, Array.Empty<string>());
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        var wrapped = await ReadAsync<List<T>>(response, cancellationToken);
        return wrapped ?? new List<T>();
    }

    /// <summary>GET expecting a wrapped single resource; returns null on 404.</summary>
    private async Task<T?> GetSingleAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        _logger.LogInformation("Maxio API request: {Method} {Path}", method, path);
        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var errors = ExtractErrors(body);
            var message = $"Maxio API call failed ({(int)response.StatusCode} {response.StatusCode}) for {method} {path}.";
            _logger.LogError("Maxio API call failed: {StatusCode} {Path} {Body}", (int)response.StatusCode, path, body);
            throw new MaxioApiException(message, (int)response.StatusCode, errors);
        }

        return response;
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }
        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    /// <summary>Maxio error bodies are either {"errors":[...]} or {"error":"..."}.</summary>
    private static IReadOnlyList<string> ExtractErrors(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("errors", out var errorsElement) &&
                errorsElement.ValueKind == JsonValueKind.Array)
            {
                return errorsElement.EnumerateArray()
                    .Select(e => e.ToString())
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .ToList();
            }
            if (root.TryGetProperty("error", out var errorElement) &&
                errorElement.ValueKind == JsonValueKind.String)
            {
                return new List<string> { errorElement.GetString()! };
            }
        }
        catch (JsonException)
        {
            // fall through to raw body
        }
        return new List<string> { body };
    }
}
