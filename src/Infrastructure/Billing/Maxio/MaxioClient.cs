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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// HTTP implementation of IMaxioClient against the Maxio Advanced Billing API,
/// built to the OpenAPI specification in maxio-spec/.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioOptions> _options;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        return Deserialize<MaxioCustomerResponse>(body).Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Post, "customers.json", cancellationToken,
            content: JsonContent.Create(new MaxioCreateCustomerRequest { Customer = customer }, options: MaxioJson.Options));
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        return Deserialize<MaxioCustomerResponse>(body).Customer;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyIdOrHandle, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"product_families/{Uri.EscapeDataString(productFamilyIdOrHandle)}/products.json",
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        return Deserialize<List<MaxioProductResponse>>(body).Select(r => r.Product).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreate subscription, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Post, "subscriptions.json", cancellationToken,
            content: JsonContent.Create(new MaxioCreateSubscriptionRequest { Subscription = subscription }, options: MaxioJson.Options));
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        return Deserialize<MaxioSubscriptionResponse>(body).Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        return Deserialize<List<MaxioSubscriptionResponse>>(body).Select(r => r.Subscription).ToList();
    }

    public async Task<MaxioSubscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"subscriptions/{subscriptionId}.json", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
        return Deserialize<MaxioSubscriptionResponse>(body).Subscription;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUrl, CancellationToken cancellationToken, HttpContent? content = null)
    {
        var options = _options.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new MaxioConfigurationException("Maxio API key is not configured (Maxio:ApiKey).");
        }

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(options.ResolveBaseUrl() + "/");
        }

        var request = new HttpRequestMessage(method, relativeUrl);
        // Basic auth per the OpenAPI securitySchemes: username = API key, password = "x"
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (content != null)
        {
            request.Content = content;
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static T Deserialize<T>(string body)
    {
        try
        {
            var result = JsonSerializer.Deserialize<T>(body, MaxioJson.Options);
            if (result is null)
            {
                throw new MaxioApiException(200, body, $"Maxio API returned an empty response where {typeof(T).Name} was expected.");
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(200, body, $"Maxio API returned an unexpected response format: {ex.Message}");
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new MaxioApiException((int)response.StatusCode, body, errors: ParseErrors(body));
    }

    /// <summary>
    /// Parses Maxio error bodies per the spec's error models: a single {"error": "..."},
    /// a list {"errors": ["a", "b"]}, a map {"errors": {"field": "message"}}, or nested
    /// maps like {"errors": {"subscription": ["..."]}}.
    /// </summary>
    private static IReadOnlyList<string>? ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var errors = new List<string>();

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out var singleError) && singleError.ValueKind == JsonValueKind.String)
                {
                    errors.Add(singleError.GetString()!);
                }

                if (root.TryGetProperty("errors", out var errorsElement))
                {
                    CollectErrors(errorsElement, errors);
                }
            }
            else if (root.ValueKind == JsonValueKind.String)
            {
                errors.Add(root.GetString()!);
            }

            return errors.Count > 0 ? errors : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void CollectErrors(JsonElement element, List<string> errors)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                errors.Add(element.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectErrors(item, errors);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectErrors(property.Value, errors);
                }

                break;
        }
    }
}