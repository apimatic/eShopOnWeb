using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown internally when Maxio rejects a POST as a duplicate submission (HTTP 409 via the
/// uniqueness_token guard). The billing service catches this to reconcile the winning record.
/// </summary>
internal sealed class MaxioDuplicateSubmissionException : Exception
{
    public MaxioDuplicateSubmissionException(string message) : base(message) { }
}

/// <summary>
/// Low-level typed HTTP client for the Maxio Advanced Billing REST API. Owns authentication
/// (HTTP Basic: API key as username, "X" as password), JSON (snake_case) serialization,
/// transient-fault retries, and mapping of upstream failures to domain exceptions. It knows
/// nothing about eShopOnWeb concepts — that orchestration lives in
/// <see cref="MaxioSubscriptionBillingService"/>.
/// </summary>
internal sealed class MaxioApiClient
{
    private const int MaxAttempts = 4;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;
    private readonly string _productFamilyHandle;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var config = settings.Value;
        // Validates required settings (throws BillingConfigurationException when misconfigured).
        var baseUri = config.ResolveBaseUri();
        _productFamilyHandle = config.ProductFamilyHandle!;

        // Ensure a trailing slash so relative request paths combine correctly.
        _httpClient.BaseAddress = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(60);

        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic);
        _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Returns the customer with the given app reference, or <c>null</c> if none exists.</summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "customer lookup", cancellationToken);
        var envelope = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope.Customer;
    }

    /// <summary>Creates a customer. <paramref name="uniquenessToken"/> makes the POST safe to retry.</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerBody body, string uniquenessToken, CancellationToken cancellationToken)
    {
        var payload = new CreateCustomerRequest { Customer = body, UniquenessToken = uniquenessToken };
        using var response = await SendWithRetryAsync(() => JsonRequest(HttpMethod.Post, "customers.json", payload), cancellationToken);

        ThrowIfDuplicate(response, "customer creation");
        await EnsureSuccessAsync(response, "customer creation", cancellationToken);

        var envelope = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope.Customer ?? throw new BillingUpstreamException("Maxio customer creation returned no customer.");
    }

    /// <summary>Lists the (non-archived filtering is left to the caller) products of the configured product family.</summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(_productFamilyHandle)}/products.json?per_page=200";
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingUpstreamException(
                $"Maxio product family '{_productFamilyHandle}' was not found. Check the Maxio:ProductFamilyHandle setting.");
        }

        await EnsureSuccessAsync(response, "list family products", cancellationToken);

        var envelopes = await DeserializeAsync<List<MaxioProductEnvelope>>(response, cancellationToken);
        var products = new List<MaxioProduct>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    /// <summary>Lists all subscriptions belonging to a customer.</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);

        var envelopes = await DeserializeAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
        var subscriptions = new List<MaxioSubscription>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    /// <summary>
    /// Creates a subscription. Throws <see cref="MaxioDuplicateSubmissionException"/> on a 409
    /// duplicate (uniqueness_token guard) so the caller can reconcile the winning subscription.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionBody body, string uniquenessToken, CancellationToken cancellationToken)
    {
        var payload = new CreateSubscriptionRequest { Subscription = body, UniquenessToken = uniquenessToken };
        using var response = await SendWithRetryAsync(() => JsonRequest(HttpMethod.Post, "subscriptions.json", payload), cancellationToken);

        ThrowIfDuplicate(response, "subscription creation");
        await EnsureSuccessAsync(response, "subscription creation", cancellationToken);

        var envelope = await DeserializeAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope.Subscription ?? throw new BillingUpstreamException("Maxio subscription creation returned no subscription.");
    }

    private HttpRequestMessage JsonRequest(HttpMethod method, string path, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return new HttpRequestMessage(method, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// Sends a request, retrying transient failures (network errors, timeouts, 429 and 5xx) with
    /// exponential backoff. All POSTs carry a uniqueness_token, so retries cannot duplicate work.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                using var request = requestFactory();
                response = await _httpClient.SendAsync(request, cancellationToken);

                if (!IsTransient(response.StatusCode) || attempt >= MaxAttempts)
                {
                    return response;
                }

                _logger.LogWarning(
                    "Maxio request {Method} {Path} returned transient {StatusCode}; retrying (attempt {Attempt}/{Max}).",
                    request.Method, request.RequestUri, (int)response.StatusCode, attempt, MaxAttempts);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // genuine caller cancellation — do not retry
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                if (attempt >= MaxAttempts)
                {
                    throw new BillingUpstreamException("Maxio is unreachable or timed out.", ex);
                }

                _logger.LogWarning(ex, "Maxio request failed transiently; retrying (attempt {Attempt}/{Max}).", attempt, MaxAttempts);
            }

            response?.Dispose();
            await Task.Delay(BackoffFor(attempt), cancellationToken);
        }
    }

    private static TimeSpan BackoffFor(int attempt) => TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static void ThrowIfDuplicate(HttpResponseMessage response, string operation)
    {
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new MaxioDuplicateSubmissionException($"Maxio reported a duplicate submission during {operation}.");
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await SafeReadAsync(response, cancellationToken);
        var detail = ExtractErrorMessages(body);
        _logger.LogError("Maxio {Operation} failed: {StatusCode} {Detail}", operation, (int)response.StatusCode, detail);
        throw new BillingUpstreamException(
            $"Maxio {operation} failed with status {(int)response.StatusCode} ({response.StatusCode}). {detail}".TrimEnd());
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var result = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return result ?? throw new BillingUpstreamException("Maxio returned an empty response body.");
        }
        catch (JsonException ex)
        {
            throw new BillingUpstreamException("Maxio returned a response that could not be parsed.", ex);
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Best-effort extraction of Maxio's <c>{"errors": [...]}</c> / <c>{"errors": {..}}</c> payload.</summary>
    private static string ExtractErrorMessages(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errors))
            {
                var messages = new List<string>();
                switch (errors.ValueKind)
                {
                    case JsonValueKind.Array:
                        foreach (var item in errors.EnumerateArray())
                        {
                            messages.Add(item.ToString());
                        }
                        break;
                    case JsonValueKind.Object:
                        foreach (var property in errors.EnumerateObject())
                        {
                            messages.Add($"{property.Name}: {property.Value}");
                        }
                        break;
                    default:
                        messages.Add(errors.ToString());
                        break;
                }

                return string.Join("; ", messages);
            }
        }
        catch (JsonException)
        {
            // fall through to returning the raw body (trimmed)
        }

        return body.Length > 500 ? body[..500] : body;
    }
}
