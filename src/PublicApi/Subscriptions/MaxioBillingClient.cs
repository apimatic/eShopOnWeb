using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Small, spec-shaped Maxio client. The paths and JSON models intentionally mirror
/// maxio-spec/openapi.yaml rather than relying on an SDK with a separate contract.
/// </summary>
public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json",
            null,
            cancellationToken);
        var products = await response.Content.ReadFromJsonAsync<List<MaxioProductResponse>>(JsonOptions, cancellationToken)
                       ?? new List<MaxioProductResponse>();
        return products.Where(x => x.Product is not null).Select(x => x.Product).ToArray();
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            cancellationToken,
            allowNotFound: true);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions, cancellationToken);
        return result?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post,
            "customers.json",
            new MaxioCustomerRequest { Customer = customer },
            cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions, cancellationToken);
        return result?.Customer ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty customer response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);
        var subscriptions = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionResponse>>(JsonOptions, cancellationToken)
                            ?? new List<MaxioSubscriptionResponse>();
        return subscriptions.Where(x => x.Subscription is not null).Select(x => x.Subscription).ToArray();
    }

    public async Task<MaxioSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"subscriptions/{subscriptionId}.json",
            null,
            cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(JsonOptions, cancellationToken);
        return result?.Subscription ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty subscription response.");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post,
            "subscriptions.json",
            new MaxioSubscriptionRequest
            {
                Subscription = new MaxioSubscriptionAttributes
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = "remittance",
                    Reference = reference
                }
            },
            cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(JsonOptions, cancellationToken);
        return result?.Subscription ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty subscription response.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        var options = _options.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl) && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is not configured.");
        }

        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? $"https://{options.Subdomain}.chargify.com/"
            : options.BaseUrl!;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTP or HTTPS URL.");
        }

        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        using var request = new HttpRequestMessage(method, new Uri(new Uri(baseUrl), relativePath));
        var basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
        {
            return response;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var statusCode = response.StatusCode;
        response.Dispose();
        _logger.LogWarning("Maxio request {Method} {Path} failed with status {StatusCode}.", method, relativePath, (int)statusCode);
        throw new MaxioApiException(statusCode, ExtractErrorMessage(errorBody, statusCode));
    }

    private static string ExtractErrorMessage(string body, HttpStatusCode statusCode)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("errors", out var errors))
                {
                    if (errors.ValueKind == JsonValueKind.Array)
                    {
                        return string.Join("; ", errors.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)));
                    }

                    if (errors.ValueKind == JsonValueKind.Object)
                    {
                        return string.Join("; ", errors.EnumerateObject().Select(x => $"{x.Name}: {x.Value}"));
                    }
                }
            }
            catch (JsonException)
            {
                // Keep the exception safe and useful when an upstream proxy returns non-JSON.
            }
        }

        return $"Maxio request failed with HTTP {(int)statusCode} ({statusCode}).";
    }
}
