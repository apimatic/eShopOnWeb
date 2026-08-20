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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for Maxio Advanced Billing (Chargify). Authenticated with HTTP Basic
/// (API key as username, "X" as password) per Billing API authentication docs.
/// </summary>
public class MaxioAdvancedBillingClient : IMaxioBillingGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BillingProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var products = new List<BillingProduct>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?page={page}&per_page={perPage}&include_archived=false";
            var wrappers = await SendAsync<List<MaxioProductWrapper>>(HttpMethod.Get, path, null, cancellationToken)
                           ?? new List<MaxioProductWrapper>();

            foreach (var wrapper in wrappers)
            {
                if (wrapper.Product is not null)
                {
                    products.Add(wrapper.Product.ToBillingProduct());
                }
            }

            if (wrappers.Count < perPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<BillingProduct?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var wrapper = await SendAsync<MaxioProductWrapper>(
                HttpMethod.Get,
                $"products/handle/{Uri.EscapeDataString(productHandle)}.json",
                null,
                cancellationToken);
            return wrapper?.Product?.ToBillingProduct();
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var wrapper = await SendAsync<MaxioCustomerWrapper>(
                HttpMethod.Get,
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
                null,
                cancellationToken);
            return wrapper?.Customer?.ToBillingCustomer();
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerPayload
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var wrapper = await SendAsync<MaxioCustomerWrapper>(HttpMethod.Post, "customers.json", body, cancellationToken);
        if (wrapper?.Customer is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio created a customer but returned an empty body.");
        }

        return wrapper.Customer.ToBillingCustomer();
    }

    public async Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var wrapper = await SendAsync<MaxioSubscriptionWrapper>(
                HttpMethod.Get,
                $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
                null,
                cancellationToken);
            return wrapper?.Subscription?.ToBillingSubscription();
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(
        string productHandle,
        int customerId,
        string subscriptionReference,
        string uniquenessToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await CreateSubscriptionWithCollectionMethodAsync(
                productHandle, customerId, subscriptionReference, uniquenessToken, "remittance", cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity
                                           && ex.Message.Contains("payment_collection_method", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Retrying subscription create with invoice collection after remittance was rejected.");
            return await CreateSubscriptionWithCollectionMethodAsync(
                productHandle,
                customerId,
                subscriptionReference,
                Guid.NewGuid().ToString("D"),
                "invoice",
                cancellationToken);
        }
    }

    private async Task<BillingSubscription> CreateSubscriptionWithCollectionMethodAsync(
        string productHandle,
        int customerId,
        string subscriptionReference,
        string uniquenessToken,
        string paymentCollectionMethod,
        CancellationToken cancellationToken)
    {
        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionPayload
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = subscriptionReference,
                PaymentCollectionMethod = paymentCollectionMethod
            },
            UniquenessToken = uniquenessToken
        };

        var wrapper = await SendAsync<MaxioSubscriptionWrapper>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        if (wrapper?.Subscription is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio created a subscription but returned an empty body.");
        }

        return wrapper.Subscription.ToBillingSubscription();
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var wrappers = await SendAsync<List<MaxioSubscriptionWrapper>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken) ?? new List<MaxioSubscriptionWrapper>();

        var subscriptions = new List<BillingSubscription>(wrappers.Count);
        foreach (var wrapper in wrappers)
        {
            if (wrapper.Subscription is not null)
            {
                subscriptions.Add(wrapper.Subscription.ToBillingSubscription());
            }
        }

        return subscriptions;
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string relativePath, object? body, CancellationToken cancellationToken)
    {
        EnsureReady();

        var maxAttempts = method == HttpMethod.Get ? 3 : 1;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = BuildRequest(method, relativePath, body);
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new MaxioApiException(HttpStatusCode.GatewayTimeout, "Timed out calling Maxio Billing API.");
            }
            catch (HttpRequestException ex)
            {
                throw new MaxioApiException(HttpStatusCode.BadGateway, $"Unable to reach Maxio Billing API: {ex.Message}");
            }

            using (response)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                if ((int)response.StatusCode == 429 && attempt < maxAttempts)
                {
                    _logger.LogWarning("Maxio returned 429 for {Method} {Path}; retrying ({Attempt}/{Max}).", method, relativePath, attempt, maxAttempts);
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                    continue;
                }

                if (response.IsSuccessStatusCode)
                {
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        return default;
                    }

                    try
                    {
                        return JsonSerializer.Deserialize<T>(content, JsonOptions);
                    }
                    catch (JsonException ex)
                    {
                        lastException = ex;
                        throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned a response that could not be parsed.");
                    }
                }

                var message = ReadErrorMessage(content, response.StatusCode);
                _logger.LogWarning("Maxio {Method} {Path} failed with {Status}: {Message}", method, SanitizePath(relativePath), (int)response.StatusCode, message);
                throw new MaxioApiException(response.StatusCode, message);
            }
        }

        throw lastException ?? new MaxioApiException(HttpStatusCode.BadGateway, "Maxio request failed.");
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string relativePath, object? body)
    {
        var request = new HttpRequestMessage(method, relativePath);
        var apiKey = _options.Value.ApiKey;
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:X"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private void EnsureReady()
    {
        var options = _options.Value;
        if (!options.IsConfigured)
        {
            throw new MaxioConfigurationException("Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.");
        }

        _httpClient.BaseAddress ??= options.GetApiBaseAddress();
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout == TimeSpan.Zero)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(100);
        }
    }

    private static string SanitizePath(string path)
    {
        var queryIndex = path.IndexOf('?');
        return queryIndex >= 0 ? path[..queryIndex] : path;
    }

    private static string ReadErrorMessage(string content, HttpStatusCode statusCode)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return $"Maxio Billing API returned {(int)statusCode}.";
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    var parts = new List<string>();
                    foreach (var item in errors.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            parts.Add(item.GetString() ?? string.Empty);
                        }
                        else
                        {
                            parts.Add(item.ToString());
                        }
                    }

                    var joined = string.Join("; ", parts);
                    if (!string.IsNullOrWhiteSpace(joined))
                    {
                        return joined;
                    }
                }
                else if (errors.ValueKind == JsonValueKind.Object || errors.ValueKind == JsonValueKind.String)
                {
                    return errors.ToString();
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body from Maxio; fall through to a generic message.
        }

        return $"Maxio Billing API returned {(int)statusCode}.";
    }
}
