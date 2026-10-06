using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing API. Contract: maxio-spec/openapi.yaml
/// (Basic auth with the API key as username and "x" as password; JSON envelopes
/// {"customer": ...}, {"subscription": ...}; error bodies as {"errors": [...]}).
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<HttpStatusCode> TransientStatusCodes = new()
    {
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    };

    private const int MaxAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, HttpStatusCode.NotFound, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<CustomerEnvelope, CreateCustomerEnvelope>(HttpMethod.Post, "customers.json", new CreateCustomerEnvelope { Customer = customer }, cancellationToken);
        return Require(envelope?.Customer, "customer");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"product_families/{Uri.EscapeDataString($"handle:{familyHandle}")}/products.json", null, cancellationToken);
        var envelopes = await ReadAsync<List<ProductEnvelope>>(response, cancellationToken);
        return envelopes?.Where(e => e?.Product is not null).Select(e => e!.Product!).ToList()
            ?? new List<MaxioProduct>();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<SubscriptionEnvelope, CreateSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", new CreateSubscriptionEnvelope { Subscription = subscription }, cancellationToken);
        return Require(envelope?.Subscription, "subscription");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", null, cancellationToken);
        var envelopes = await ReadAsync<List<SubscriptionEnvelope>>(response, cancellationToken);
        return envelopes?.Where(e => e?.Subscription is not null).Select(e => e!.Subscription!).ToList()
            ?? new List<MaxioSubscription>();
    }

    private static T Require<T>(T? value, string resourceName) where T : class
    {
        return value ?? throw new MaxioApiException(500, new[] { $"Maxio API returned an empty {resourceName}." });
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var response = await SendCoreAsync(method, path, body, cancellationToken);
        await EnsureSuccessAsync(response, path, cancellationToken);
        return response;
    }

    /// <summary>
    /// Sends a request, treating <paramref name="passThroughStatusCode"/> as a
    /// non-exceptional outcome the caller handles itself (e.g. 404 on lookup).
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, HttpStatusCode passThroughStatusCode, CancellationToken cancellationToken)
    {
        var response = await SendCoreAsync(method, path, body, cancellationToken);
        if (response.StatusCode != passThroughStatusCode)
        {
            await EnsureSuccessAsync(response, path, cancellationToken);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        _options.RequireValid();

        var apiKey = _options.ApiKey!.Trim();

        HttpResponseMessage response = null!;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, body.GetType(), SerializerOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:x")));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            if (!TransientStatusCodes.Contains(response.StatusCode) || attempt == MaxAttempts)
            {
                break;
            }

            var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
            _logger.LogWarning(
                "Maxio API call {Method} {Path} returned {StatusCode}; retrying in {Delay}.",
                method, path, (int)response.StatusCode, delay);
            await Task.Delay(delay, cancellationToken);
        }

        return response;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ParseErrors(errorBody, $"HTTP {(int)response.StatusCode} calling Maxio API {path}.");
        throw new MaxioApiException((int)response.StatusCode, errors);
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);
    }

    private async Task<TResponse> SendAsync<TResponse, TBody>(HttpMethod method, string path, TBody body, CancellationToken cancellationToken)
        where TResponse : class
    {
        var response = await SendAsync(method, path, body, cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken)
            ?? throw new MaxioApiException(500, new[] { $"Maxio API returned an empty response for {path}." });
    }

    /// <summary>
    /// Normalizes the error models the spec allows: {"errors": [...]} (string array),
    /// {"errors": {field: message}} (string map), {"error": "..."} (single), or raw text.
    /// </summary>
    public static IReadOnlyList<string> ParseErrors(string? errorBody, string fallback)
    {
        if (string.IsNullOrWhiteSpace(errorBody))
        {
            return new[] { fallback };
        }

        try
        {
            using var document = JsonDocument.Parse(errorBody);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("errors", out var errors))
                {
                    switch (errors.ValueKind)
                    {
                        case JsonValueKind.Array when errors.GetArrayLength() > 0:
                            return errors.EnumerateArray().Select(e => e.ToString()).ToList();
                        case JsonValueKind.Object:
                            {
                                var messages = errors.EnumerateObject()
                                    .Select(p => p.Value.ValueKind == JsonValueKind.String
                                        ? $"{p.Name}: {p.Value.GetString()}"
                                        : $"{p.Name}: {p.Value}")
                                    .ToList();
                                if (messages.Count > 0)
                                {
                                    return messages;
                                }

                                break;
                            }
                        case JsonValueKind.String when !string.IsNullOrWhiteSpace(errors.GetString()):
                            return new[] { errors.GetString()! };
                    }
                }

                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                {
                    return new[] { error.GetString() ?? fallback };
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw body below.
        }

        return new[] { errorBody.Trim() };
    }
}