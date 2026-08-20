using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Thin HTTP client for the Billing API (Maxio Advanced Billing).
/// Authentication is HTTP Basic over TLS: API key as username, "X" as password.
/// </summary>
internal sealed class MaxioApiClient
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        var familyKey = Uri.EscapeDataString($"handle:{familyHandle}");
        var envelopes = await GetAsync<List<MaxioProductEnvelope>>(
            $"product_families/{familyKey}/products.json?per_page=200",
            cancellationToken);

        var products = new List<MaxioProduct>();
        if (envelopes is null)
        {
            return products;
        }

        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        return (await GetOptionalAsync<MaxioCustomerEnvelope>(path, cancellationToken))?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        var envelope = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope>(
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            cancellationToken);

        if (envelope.Customer is null)
        {
            throw new BillingException("Maxio created a customer but returned no customer payload.");
        }

        return envelope.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken);

        var subscriptions = new List<MaxioSubscription>();
        if (envelopes is null)
        {
            return subscriptions;
        }

        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        return (await GetOptionalAsync<MaxioSubscriptionEnvelope>(path, cancellationToken))?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        string uniquenessToken,
        CancellationToken cancellationToken)
    {
        var envelope = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope>(
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest
            {
                Subscription = subscription,
                UniquenessToken = uniquenessToken
            },
            cancellationToken);

        if (envelope.Subscription is null)
        {
            throw new BillingException("Maxio created a subscription but returned no subscription payload.");
        }

        return envelope.Subscription;
    }

    private async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, relativePath),
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<T?> GetOptionalAsync<T>(string relativePath, CancellationToken cancellationToken) where T : class
    {
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, relativePath),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string relativePath,
        TRequest body,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, relativePath)
            {
                Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
            },
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new MaxioConflictException(await response.Content.ReadAsStringAsync(cancellationToken));
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioUnprocessableException(FormatError(payload), payload);
        }

        if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.Created)
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }

        var result = await ReadJsonAsync<TResponse>(response, cancellationToken);
        if (result is null)
        {
            throw new BillingException("Maxio returned an empty JSON payload.");
        }

        return result;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> factory, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        HttpResponseMessage? response = null;
        const int maxAttempts = 4;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            response?.Dispose();
            using var request = factory();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            response = await _httpClient.SendAsync(request, cancellationToken);

            if ((int)response.StatusCode != 429 || attempt == maxAttempts)
            {
                return response;
            }

            var delay = TimeSpan.FromSeconds(2 * attempt);
            _logger.LogWarning(
                "Maxio returned 429 Too Many Requests; waiting {Delay}s before retry {Attempt}/{Max}.",
                delay.TotalSeconds,
                attempt,
                maxAttempts);
            await Task.Delay(delay, cancellationToken);
        }

        return response!;
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new BillingConfigurationException(
                "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle (and optionally Maxio:BaseUrl).");
        }

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = _options.GetApiBaseAddress();
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = FormatError(body);
        throw new BillingException(
            $"Maxio Billing API request failed ({(int)response.StatusCode} {response.StatusCode}): {message}",
            response.StatusCode);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    internal static string FormatError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return "No error payload.";
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                return errors.ValueKind switch
                {
                    JsonValueKind.Array => string.Join("; ", FlattenArray(errors)),
                    JsonValueKind.Object => errors.ToString(),
                    JsonValueKind.String => errors.GetString() ?? payload,
                    _ => payload
                };
            }
        }
        catch (JsonException)
        {
            // Fall through and return the raw payload.
        }

        return payload;
    }

    private static IEnumerable<string> FlattenArray(JsonElement array)
    {
        foreach (var item in array.EnumerateArray())
        {
            yield return item.ValueKind == JsonValueKind.String ? item.GetString() ?? item.GetRawText() : item.GetRawText();
        }
    }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new();

    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

internal sealed class MaxioConflictException : BillingException
{
    public MaxioConflictException(string payload)
        : base("A duplicate Maxio request was detected (409 Conflict).", HttpStatusCode.Conflict)
    {
        Payload = payload;
    }

    public string Payload { get; }
}

internal sealed class MaxioUnprocessableException : BillingException
{
    public MaxioUnprocessableException(string message, string payload)
        : base(message, HttpStatusCode.UnprocessableEntity)
    {
        Payload = payload;
    }

    public string Payload { get; }
}
