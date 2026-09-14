using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level client for the Maxio Advanced Billing (formerly Chargify) HTTP API.
/// Contract sources (verified against the official Maxio Advanced Billing developer portal,
/// the official ab-dotnet-sdk, and a live sandbox site):
///   - Authentication: HTTP Basic, username = API key, password = "x".
///   - US sites:  https://{subdomain}.chargify.com
///   - EU sites:  https://{subdomain}.ebilling.maxio.com
///   - List products of a product family: GET /product_families/{id-or-"handle:" + handle}/products.json
///   - Find customer by reference:        GET /customers/lookup.json?reference={reference}  (404 when absent)
///   - Create customer:                   POST /customers.json   (customer.reference must be unique)
///   - Create subscription:               POST /subscriptions.json (product_handle + customer_id +
///                                        payment_collection_method = "remittance" for cardless signup)
///   - List a customer's subscriptions:   GET /customers/{customer_id}/subscriptions.json
/// </summary>
public interface IMaxioGateway
{
    Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(string familyHandle, CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(string email, string firstName, string lastName, string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}

public class MaxioGateway : IMaxioGateway
{
    private const string RemittanceCollectionMethod = "remittance";
    private const int MaxRetries = 3;

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioGateway> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public MaxioGateway(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioGateway> logger)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.Subdomain) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio Advanced Billing is not configured. Set the Maxio:ApiKey, Maxio:Subdomain and " +
                "Maxio:ProductFamilyHandle configuration keys (via user-secrets or environment variables).");
        }
        _logger = logger;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Authorization = CreateBasicAuthHeader(_options.ApiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(string familyHandle, CancellationToken cancellationToken)
    {
        const int perPage = 100;
        var products = new List<MaxioProduct>();
        for (int page = 1; ; page++)
        {
            var response = await SendAsync(HttpMethod.Get,
                $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?page={page}&per_page={perPage}",
                requestPayload: null,
                cancellationToken);
            var pageItems = ParseWrappedList<MaxioProduct>(response, "product");
            products.AddRange(pageItems);
            if (pageItems.Count < perPage)
            {
                break;
            }
        }
        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAsync(HttpMethod.Get,
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
                requestPayload: null,
                cancellationToken);
            return ParseWrapped<MaxioCustomer>(response, "customer");
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string email, string firstName, string lastName, string reference, CancellationToken cancellationToken)
    {
        var payload = new CreateCustomerRequest(new CreateCustomerBody(
            FirstName: firstName,
            LastName: lastName,
            Email: email,
            Reference: reference));

        var response = await SendAsync(HttpMethod.Post, "customers.json", payload, cancellationToken);
        return ParseWrapped<MaxioCustomer>(response, "customer");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var payload = new CreateSubscriptionRequest(new CreateSubscriptionBody(
            ProductHandle: productHandle,
            CustomerId: customerId,
            PaymentCollectionMethod: RemittanceCollectionMethod));

        var response = await SendAsync(HttpMethod.Post, "subscriptions.json", payload, cancellationToken);
        return ParseWrapped<MaxioSubscription>(response, "subscription");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            requestPayload: null,
            cancellationToken);
        return ParseWrappedList<MaxioSubscription>(response, "subscription");
    }

    private static AuthenticationHeaderValue CreateBasicAuthHeader(string apiKey)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }

    private async Task<string> SendAsync(HttpMethod method, string relativeUrl, object? requestPayload, CancellationToken cancellationToken)
    {
        var baseAddress = ResolveBaseAddress();
        var url = $"{baseAddress.TrimEnd('/')}/{relativeUrl}";

        for (int attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);
            if (requestPayload != null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(requestPayload, JsonOptions), Encoding.UTF8, "application/json");
            }

            string? responseBody;
            int statusCode;
            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                statusCode = (int)response.StatusCode;
                responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return responseBody;
                }
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries && IsRetryable(method))
            {
                _logger.LogWarning(ex, "Transient error calling Maxio API {Url} (attempt {Attempt}/{MaxRetries}).", relativeUrl, attempt, MaxRetries);
                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                continue;
            }

            // GET requests are safe to retry on server-side failures; POSTs are not retried
            // because Advanced Billing offers no idempotency-key mechanism.
            if (attempt < MaxRetries && IsRetryable(method) && statusCode >= 500)
            {
                _logger.LogWarning("Maxio API returned {StatusCode} for {Url} (attempt {Attempt}/{MaxRetries}).", statusCode, relativeUrl, attempt, MaxRetries);
                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                continue;
            }

            throw new MaxioApiException(
                $"Maxio API call {method.Method} {relativeUrl} failed with status {statusCode}.",
                statusCode,
                responseBody);
        }
    }

    private string ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return _options.BaseUrl;
        }

        var subdomain = Uri.EscapeDataString(_options.Subdomain);
        return string.Equals(_options.Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"https://{subdomain}.ebilling.maxio.com"
            : $"https://{subdomain}.chargify.com";
    }

    private static bool IsRetryable(HttpMethod method) => method == HttpMethod.Get;

    private static TimeSpan GetRetryDelay(int attempt) =>
        TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 150));

    private static T ParseWrapped<T>(string responseBody, string wrapperKey)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty(wrapperKey, out var element))
        {
            throw new MaxioApiException(
                $"Maxio API response did not contain the expected '{wrapperKey}' resource.",
                responseBody: responseBody);
        }
        var result = element.Deserialize<T>(JsonOptions);
        if (result == null)
        {
            throw new MaxioApiException(
                $"Maxio API returned an unexpected '{wrapperKey}' resource.",
                responseBody: responseBody);
        }
        return result;
    }

    private static List<T> ParseWrappedList<T>(string responseBody, string wrapperKey)
    {
        var result = new List<T>();
        using var document = JsonDocument.Parse(responseBody);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty(wrapperKey, out var element) &&
                element.Deserialize<T>(JsonOptions) is { } value)
            {
                result.Add(value);
            }
        }
        return result;
    }
}
