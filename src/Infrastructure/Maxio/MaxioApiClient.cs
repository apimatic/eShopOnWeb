using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level typed client for the Maxio Advanced Billing (Billing API) REST surface.
/// Handles Basic authentication (API key as username, "x" as password), snake_case
/// JSON, and retry of transient failures. Endpoint shapes come exclusively from the
/// Maxio Billing API documentation.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>Lists every product on the site, following pagination.</summary>
    Task<IReadOnlyList<MaxioProductDto>> ListProductsAsync(CancellationToken cancellationToken);

    /// <summary>Reads a product by its API handle; null when the handle does not exist.</summary>
    Task<MaxioProductDto?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken);

    /// <summary>Reads a customer by reference; null when no customer has that reference.</summary>
    Task<MaxioCustomerDto?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>Creates a customer. Fails with <see cref="MaxioApiException"/> if the reference is taken.</summary>
    Task<MaxioCustomerDto> CreateCustomerAsync(MaxioNewCustomer customer, CancellationToken cancellationToken);

    /// <summary>Lists every subscription that belongs to a customer.</summary>
    Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a subscription for a customer on a product. The uniqueness token makes
    /// a retried request be rejected as a duplicate (409) instead of creating twice.
    /// </summary>
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(string productHandle, int customerId, string uniquenessToken, CancellationToken cancellationToken);
}

public sealed class MaxioApiClient : IMaxioApiClient
{
    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
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

    public async Task<IReadOnlyList<MaxioProductDto>> ListProductsAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var products = new List<MaxioProductDto>();
        const int perPage = 200;
        int page = 1;

        while (true)
        {
            var batch = await GetAsync<List<MaxioProductResponse>>(
                $"products.json?per_page={perPage}&page={page}", cancellationToken);
            products.AddRange(batch.Where(r => r.Product != null).Select(r => r.Product!));
            if (batch.Count < perPage)
            {
                return products;
            }
            page++;
        }
    }

    public async Task<MaxioProductDto?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return (await GetAsync<MaxioProductResponse>($"products/handle/{Uri.EscapeDataString(handle)}.json",
                cancellationToken, notFoundIsNull: true))?.Product;
    }

    public async Task<MaxioCustomerDto?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return (await GetAsync<MaxioCustomerResponse>(
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
                cancellationToken, notFoundIsNull: true))?.Customer;
    }

    public async Task<MaxioCustomerDto> CreateCustomerAsync(MaxioNewCustomer customer, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer }, cancellationToken);
        return Require(response.Customer, "customer");
    }

    public async Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var subscriptions = new List<MaxioSubscriptionDto>();
        const int perPage = 200;
        int page = 1;

        while (true)
        {
            var batch = await GetAsync<List<MaxioSubscriptionResponse>>(
                $"customers/{customerId}/subscriptions.json?per_page={perPage}&page={page}", cancellationToken);
            subscriptions.AddRange(batch.Where(r => r.Subscription != null).Select(r => r.Subscription!));
            if (batch.Count < perPage)
            {
                return subscriptions;
            }
            page++;
        }
    }

    /// <summary>
    /// Default collection methods attempted for a signup without a payment profile,
    /// in order. "remittance" is valid on Relationship Invoicing sites, "invoice" on
    /// statement-based sites; the first attempt that is not rejected as an invalid
    /// payment_collection_method wins.
    /// </summary>
    private static readonly string[] DefaultCollectionMethods = { "remittance", "invoice" };

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(
        string productHandle, int customerId, string uniquenessToken, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var collectionMethods = !string.IsNullOrWhiteSpace(_options.PaymentCollectionMethod)
            ? new[] { _options.PaymentCollectionMethod.Trim() }
            : DefaultCollectionMethods;

        MaxioApiException? lastError = null;
        foreach (var (method, index) in collectionMethods.Select((m, i) => (m, i)))
        {
            try
            {
                return await CreateSubscriptionWithCollectionMethodAsync(
                    productHandle, customerId, uniquenessToken, method, cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 422 && index < collectionMethods.Length - 1)
            {
                // Wrong site architecture for this collection method; try the next one.
                lastError = ex;
                _logger.LogInformation(
                    "Subscription create rejected with payment_collection_method '{Method}' ({Errors}); retrying with fallback.",
                    method, string.Join("; ", ex.Errors));
            }
        }

        throw lastError!;
    }

    private async Task<MaxioSubscriptionDto> CreateSubscriptionWithCollectionMethodAsync(
        string productHandle, int customerId, string uniquenessToken, string paymentCollectionMethod, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json",
            new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioNewSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = paymentCollectionMethod
                },
                UniquenessToken = uniquenessToken
            }, cancellationToken);
        return Require(response.Subscription, "subscription");
    }

    private static T Require<T>(T? value, string resource) where T : class
    {
        return value ?? throw new MaxioApiException(
            (int)HttpStatusCode.InternalServerError,
            new[] { $"Billing API response did not contain a {resource}." }, null);
    }

    private void EnsureConfigured()
    {
        _options.ValidateRequiredSettings();
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = _options.ResolveBaseUrl();
        }
    }

    private async Task<T> GetAsync<T>(string requestUri, CancellationToken cancellationToken, bool notFoundIsNull = false)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.Authorization = CreateAuthHeader();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if ((int)response.StatusCode == 404 && notFoundIsNull)
                {
                    return default!;
                }

                EnsureSuccess(response.StatusCode, body);

                return JsonSerializer.Deserialize<T>(body, JsonOptions)
                    ?? throw new MaxioApiException((int)HttpStatusCode.InternalServerError,
                        new[] { $"Billing API returned an empty response for {requestUri}." }, body);
            }
            catch (MaxioApiException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Transient failure calling Maxio Billing API ({Uri}); retry {Attempt}/{Max}.",
                    requestUri, attempt, MaxAttempts);
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
            }
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string requestUri, object payload, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, requestUri)
                {
                    Content = JsonContent.Create(payload, options: JsonOptions)
                };
                request.Headers.Authorization = CreateAuthHeader();
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                EnsureSuccess(response.StatusCode, body);

                return JsonSerializer.Deserialize<T>(body, JsonOptions)
                    ?? throw new MaxioApiException((int)HttpStatusCode.InternalServerError,
                        new[] { $"Billing API returned an empty response for {requestUri}." }, body);
            }
            catch (MaxioApiException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Transient failure calling Maxio Billing API ({Uri}); retry {Attempt}/{Max}.",
                    requestUri, attempt, MaxAttempts);
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
            }
        }
    }

    private AuthenticationHeaderValue CreateAuthHeader()
    {
        // Billing API Basic auth: API key is the username, "x" is the password.
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }

    private static void EnsureSuccess(HttpStatusCode statusCode, string responseBody)
    {
        if ((int)statusCode >= 200 && (int)statusCode < 300)
        {
            return;
        }

        var errors = Array.Empty<string>();
        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(responseBody, JsonOptions);
            if (parsed?.Errors is { Length: > 0 })
            {
                errors = parsed.Errors;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with no structured errors.
        }

        throw new MaxioApiException((int)statusCode, errors, responseBody);
    }
}
