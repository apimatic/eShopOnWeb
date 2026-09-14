using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioClient : IMaxioClient
{
    private const string RemittanceCollectionMethod = "remittance";
    private const int PageSize = 100;

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _client;
    private readonly MaxioOptions _options;

    public MaxioClient(HttpClient client, IOptions<MaxioOptions> options)
    {
        _client = client;
        _options = options.Value;

        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _client.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        }
        else if (!string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            _client.BaseAddress = new Uri($"https://{_options.Subdomain}.chargify.com/");
        }

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        if (_client.DefaultRequestHeaders.Accept.Count == 0)
        {
            _client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }
    }

    public async Task<MaxioProductFamily?> FindProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        var families = await ListAllAsync<MaxioProductFamilyEnvelope>("product_families.json", cancellationToken);
        return families.Select(e => e.ProductFamily)
            .FirstOrDefault(f => f is not null && string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(() => _client.GetAsync($"products/handle/{Uri.EscapeDataString(handle)}.json", cancellationToken), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        var envelope = await ReadAsync<MaxioProductEnvelope>(response, cancellationToken);
        return envelope?.Product;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductFamilyProductsAsync(long productFamilyId, CancellationToken cancellationToken = default)
    {
        var envelopes = await ListAllAsync<MaxioProductEnvelope>($"product_families/{productFamilyId}/products.json", cancellationToken);
        return envelopes.Select(e => e.Product).Where(p => p is not null).Cast<MaxioProduct>().ToList();
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(() => _client.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateCustomerRequest(
            new MaxioCustomerAttributes(firstName, lastName, email, reference),
            Guid.NewGuid());
        var response = await SendAsync(() => _client.PostAsJsonAsync("customers.json", payload, s_jsonOptions, cancellationToken), cancellationToken);
        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken)
            ?? throw new MaxioApiException((int)response.StatusCode, Array.Empty<string>(), "Empty response body");
        return envelope.Customer ?? throw new MaxioApiException((int)response.StatusCode, Array.Empty<string>(), "Response did not contain a customer");
    }

    public async Task<MaxioSubscription?> GetSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(() => _client.GetAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, string reference, CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateSubscriptionRequest(
            new MaxioCreateSubscriptionAttributes(productHandle, customerId, RemittanceCollectionMethod, reference),
            Guid.NewGuid());
        var response = await SendAsync(() => _client.PostAsJsonAsync("subscriptions.json", payload, s_jsonOptions, cancellationToken), cancellationToken);
        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, cancellationToken)
            ?? throw new MaxioApiException((int)response.StatusCode, Array.Empty<string>(), "Empty response body");
        return envelope.Subscription ?? throw new MaxioApiException((int)response.StatusCode, Array.Empty<string>(), "Response did not contain a subscription");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var envelopes = await ListAllAsync<MaxioSubscriptionEnvelope>($"subscriptions.json?customer_id={customerId}", cancellationToken);
        return envelopes.Select(e => e.Subscription).Where(s => s is not null).Cast<MaxioSubscription>().ToList();
    }

    private async Task<List<TEnvelope>> ListAllAsync<TEnvelope>(string basePath, CancellationToken cancellationToken)
    {
        var results = new List<TEnvelope>();
        var page = 1;
        while (true)
        {
            var separator = basePath.Contains('?') ? '&' : '?';
            var path = $"{basePath}{separator}page={page}&per_page={PageSize}";
            var response = await SendAsync(() => _client.GetAsync(path, cancellationToken), cancellationToken);
            var batch = await ReadAsync<List<TEnvelope>>(response, cancellationToken) ?? new List<TEnvelope>();
            results.AddRange(batch);
            if (batch.Count < PageSize)
            {
                return results;
            }
            page++;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await send();
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException((int)HttpStatusCode.BadGateway, new[] { "Maxio Advanced Billing is unreachable: " + ex.Message });
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException((int)HttpStatusCode.GatewayTimeout, new[] { "Maxio Advanced Billing request timed out." });
        }

        if (!response.IsSuccessStatusCode && response.StatusCode is not HttpStatusCode.NotFound)
        {
            throw await BuildApiExceptionAsync(response, cancellationToken);
        }

        return response;
    }

    private async Task<MaxioApiException> BuildApiExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = new List<string>();
        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorEnvelope>(body, s_jsonOptions);
            if (parsed?.Errors is { Length: > 0 })
            {
                errors.AddRange(parsed.Errors);
            }
        }
        catch (JsonException)
        {
        }

        return new MaxioApiException((int)response.StatusCode, errors, errors.Count == 0 ? body : null);
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, s_jsonOptions, cancellationToken);
    }
}
