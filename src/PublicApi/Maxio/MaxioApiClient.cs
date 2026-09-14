using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Hand-written Maxio Advanced Billing HTTP client. Consumes the endpoints and
/// request/response schemas defined in the bundled OpenAPI specification
/// (maxio-spec/). Authentication uses HTTP Basic where the API key is the user
/// name and the password is "x", exactly as the spec's security scheme states.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private const string PasswordForBasicAuth = "x";
    private const int MaxListPerPage = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<MaxioProductEnvelope>>("products.json?per_page=" + MaxListPerPage, cancellationToken);
        return EnvelopeList(envelopes, e => e.Product);
    }

    public async Task<string?> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        var envelope = await GetAsync<MaxioSiteEnvelope>("site.json", cancellationToken);
        return envelope.Site?.Currency;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var query = "customers/lookup.json?reference=" + Uri.EscapeDataString(reference);
        var envelope = await GetOptionalAsync<MaxioCustomerEnvelope>(query, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerRequest request, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            request,
            cancellationToken);
        return envelope.Customer ?? throw new MaxioApiException(200, "Maxio returned an empty customer payload.", null);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken);
        return EnvelopeList(envelopes, e => e.Subscription);
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var query = "subscriptions/lookup.json?reference=" + Uri.EscapeDataString(reference);
        var envelope = await GetOptionalAsync<MaxioSubscriptionEnvelope>(query, cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            request,
            cancellationToken);
        return envelope.Subscription ?? throw new MaxioApiException(201, "Maxio returned an empty subscription payload.", null);
    }

    private async Task<T> GetAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<T?> GetOptionalAsync<T>(string requestUri, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await ReadAsync<T>(response, cancellationToken);
        }

        if ((int)response.StatusCode == 404)
        {
            return null;
        }

        var body = await ReadBodyAsync(response, cancellationToken);
        throw new MaxioApiException((int)response.StatusCode, MaxioApiException.FormatMessage(body), body);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string requestUri, object? payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        if (payload != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), System.Text.Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await ReadBodyAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException((int)response.StatusCode, MaxioApiException.FormatMessage(responseBody), responseBody);
        }

        return Deserialize<T>(responseBody);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException((int)response.StatusCode, MaxioApiException.FormatMessage(body), body);
        }

        return Deserialize<T>(body);
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new MaxioApiException(200, "Maxio returned an unexpected empty response body.", json);
    }

    private static IReadOnlyList<TItem> EnvelopeList<TEnvelope, TItem>(List<TEnvelope>? envelopes, Func<TEnvelope, TItem?> selector)
        where TItem : class
    {
        var items = new List<TItem>();
        if (envelopes == null)
        {
            return items;
        }

        foreach (var envelope in envelopes)
        {
            var item = selector(envelope);
            if (item != null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    /// <summary>
    /// Factory delegate for the typed <see cref="HttpClient"/> used by
    /// <see cref="MaxioApiClient"/>.
    /// </summary>
    public static void ConfigureHttpClient(HttpClient httpClient, MaxioOptions options)
    {
        httpClient.BaseAddress = new Uri(options.GetApiBaseAddress() + "/");
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credentials = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{options.ApiKey}:{PasswordForBasicAuth}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }
}
