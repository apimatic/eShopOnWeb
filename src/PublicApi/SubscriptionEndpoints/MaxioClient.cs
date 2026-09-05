using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string reference, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{Required(_settings.ProductFamilyHandle, nameof(_settings.ProductFamilyHandle))}");
        return await GetAsync<List<MaxioProductEnvelope>>($"/product_families/{family}/products.json", cancellationToken)
            .ConfigureAwait(false) is { } products
            ? products.ConvertAll(item => item.Product)
            : [];
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        await GetOrNullAsync<MaxioCustomerEnvelope>($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken)
            .ConfigureAwait(false) is { } found ? found.Customer : null;

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken)
    {
        var body = new { customer = new { first_name = firstName, last_name = lastName, email, reference } };
        return (await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "/customers.json", body, cancellationToken).ConfigureAwait(false)).Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        await GetOrNullAsync<MaxioSubscriptionEnvelope>($"/subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken)
            .ConfigureAwait(false) is { } found ? found.Subscription : null;

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string reference, CancellationToken cancellationToken)
    {
        // The configured plans may be purchased without card capture. Maxio's contract permits
        // remittance collection, which records the invoice/balance in Maxio without accepting a
        // card or invoking a 3-DS flow.
        var body = new { subscription = new { product_handle = productHandle, customer_reference = customerReference, reference, payment_collection_method = "remittance" } };
        return (await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "/subscriptions.json", body, cancellationToken).ConfigureAwait(false)).Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        return await GetAsync<List<MaxioSubscriptionEnvelope>>($"/customers/{customerId}/subscriptions.json", cancellationToken)
            .ConfigureAwait(false) is { } subscriptions
            ? subscriptions.ConvertAll(item => item.Subscription)
            : [];
    }

    private async Task<T?> GetOrNullAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        using var response = await SendRawAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return await DeserializeSuccessfulAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken) =>
        await SendAsync<T>(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(method, path, body, cancellationToken).ConfigureAwait(false);
        return await DeserializeSuccessfulAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, ApiUri(path));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Required(_settings.ApiKey, nameof(_settings.ApiKey))}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> DeserializeSuccessfulAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new MaxioApiException(response.StatusCode, payload);
        return JsonSerializer.Deserialize<T>(payload, JsonOptions)
            ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response.");
    }

    private Uri ApiUri(string path)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? $"https://{Required(_settings.Subdomain, nameof(_settings.Subdomain))}.chargify.com"
            : _settings.BaseUrl;
        return new Uri($"{baseUrl.TrimEnd('/')}{path}", UriKind.Absolute);
    }

    private static string Required(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new MaxioConfigurationException($"Maxio:{name} must be configured.");
}

public sealed class MaxioConfigurationException : Exception { public MaxioConfigurationException(string message) : base(message) { } }
public sealed class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public MaxioApiException(HttpStatusCode statusCode, string message) : base(message) => StatusCode = statusCode;
}
