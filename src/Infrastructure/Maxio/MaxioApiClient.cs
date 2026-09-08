using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly AuthenticationHeaderValue _basicAuth;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public MaxioApiClient(HttpClient httpClient, MaxioOptions options)
    {
        _httpClient = httpClient;
        _baseUrl = options.ResolveBaseUrl();
        _basicAuth = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x")));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsByFamilyHandleAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new ArgumentException("A product family handle is required.", nameof(familyHandle));
        }

        var path = $"/product_families/{EncodePathSegment($"handle:{familyHandle}")}/products.json";
        var documents = await SendListAsync<MaxioProductEnvelope>(HttpMethod.Get, path, cancellationToken);
        return documents.Select(x => x.Product).Where(x => x is not null).Cast<MaxioProduct>().ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(reference));
        }

        var path = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        if (customer is null)
        {
            throw new ArgumentNullException(nameof(customer));
        }

        using var response = await SendAsync(HttpMethod.Post, "/customers.json", new MaxioCreateCustomerEnvelope { Customer = customer }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer ?? throw CreateUnexpectedPayload(response);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"/customers/{customerId}/subscriptions.json";
        var documents = await SendListAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get, path, cancellationToken);
        return documents.Select(x => x.Subscription).Where(x => x is not null).Cast<MaxioSubscription>().ToList();
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/subscriptions/{subscriptionId}.json", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        if (subscription is null)
        {
            throw new ArgumentNullException(nameof(subscription));
        }

        using var response = await SendAsync(HttpMethod.Post, "/subscriptions.json", new MaxioCreateSubscriptionEnvelope { Subscription = subscription }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription ?? throw CreateUnexpectedPayload(response);
    }

    private async Task<List<T>> SendListAsync<T>(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadJsonAsync<List<T>>(response, cancellationToken) ?? new List<T>();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string requestUri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"{_baseUrl}{requestUri}");
        request.Headers.Authorization = _basicAuth;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException(HttpStatusCode.RequestTimeout, null,
                $"The Maxio API request timed out: {method.Method} {requestUri}.");
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, null,
                $"The Maxio API could not be reached for {method.Method} {requestUri}.", ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, body,
            $"Maxio API request to {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} failed with HTTP {(int)response.StatusCode}.");
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions);
    }

    private static MaxioApiException CreateUnexpectedPayload(HttpResponseMessage response)
    {
        return new MaxioApiException(response.StatusCode, null,
            $"The Maxio API returned an unexpected payload for {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}.");
    }

    private static string EncodePathSegment(string value)
    {
        // Uri.EscapeDataString escapes ':' which we must preserve after the "handle:" prefix.
        if (value.StartsWith("handle:", StringComparison.Ordinal))
        {
            return $"handle:{Uri.EscapeDataString(value["handle:".Length..])}";
        }

        return Uri.EscapeDataString(value);
    }
}
