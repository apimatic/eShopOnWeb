using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Contracts;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <inheritdoc />
public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<MaxioOptions> _options;

    public MaxioApiClient(HttpClient httpClient, IOptionsMonitor<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<IReadOnlyList<Product>> ListProductsInFamilyAsync(
        string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var path = $"/product_families/handle:{productFamilyHandle}/products.json";
        var items = await SendEnvelopeArrayAsync<ProductListResponseItem>(HttpMethod.Get, path, cancellationToken);
        return items.Select(i => i.Product).Where(p => p is not null).Cast<Product>().ToList();
    }

    public async Task<Product?> FindProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        var path = $"/products/handle/{productHandle}.json";
        var envelope = await SendEnvelopeAsync<ProductResponse>(HttpMethod.Get, path, cancellationToken, allowNotFound: true);
        return envelope?.Product;
    }

    public async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendEnvelopeAsync<CustomerResponse>(HttpMethod.Get, path, cancellationToken, allowNotFound: true);
        return envelope?.Customer;
    }

    public async Task<Customer> CreateCustomerAsync(CustomerAttributes attributes, CancellationToken cancellationToken = default)
    {
        var body = new CreateCustomerRequest { Customer = attributes };
        var envelope = await SendEnvelopeAsync<CustomerResponse>(HttpMethod.Post, "/customers.json", cancellationToken, body);
        return envelope?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.OK, "POST /customers.json", "Response did not contain a customer object.");
    }

    public async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"/customers/{customerId}/subscriptions.json";
        var items = await SendEnvelopeArrayAsync<SubscriptionListResponseItem>(HttpMethod.Get, path, cancellationToken);
        return items.Select(i => i.Subscription).Where(s => s is not null).Cast<Subscription>().ToList();
    }

    public async Task<Subscription> CreateSubscriptionAsync(string productHandle, int customerId, DateTimeOffset? nextBillingAt, CancellationToken cancellationToken = default)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                NextBillingAt = nextBillingAt
            }
        };
        var envelope = await SendEnvelopeAsync<SubscriptionResponse>(HttpMethod.Post, "/subscriptions.json", cancellationToken, body);
        return envelope?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.Created, "POST /subscriptions.json", "Response did not contain a subscription object.");
    }

    // ------------------------------------------------------------------
    // Plumbing
    // ------------------------------------------------------------------

    private async Task<TEnvelope?> SendEnvelopeAsync<TEnvelope>(
        HttpMethod method, string path, CancellationToken cancellationToken, object? body = null, bool allowNotFound = false)
        where TEnvelope : class
    {
        using var response = await SendAsync(method, path, cancellationToken, body);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, path, content);

        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        return JsonSerializer.Deserialize<TEnvelope>(content, MaxioJson.Options);
    }

    private async Task<IReadOnlyList<TEnvelope>> SendEnvelopeArrayAsync<TEnvelope>(
        HttpMethod method, string path, CancellationToken cancellationToken)
        where TEnvelope : class
    {
        using var response = await SendAsync(method, path, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        await EnsureSuccessAsync(response, path, content);

        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<TEnvelope>();
        }

        return JsonSerializer.Deserialize<List<TEnvelope>>(content, MaxioJson.Options) ?? new List<TEnvelope>();
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, CancellationToken cancellationToken, object? body = null)
    {
        var baseUrl = ResolveBaseUrl();
        var request = new HttpRequestMessage(method, new Uri(baseUrl + path, UriKind.Absolute));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", CreateBasicCredentials());

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, body.GetType(), MaxioJson.Options);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string path, string content)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var exception = new MaxioApiException(response.StatusCode, path, content)
        {
            ErrorMessage = MaxioErrorParser.TryExtractMessage(content)
        };
        throw exception;
    }

    private string ResolveBaseUrl()
    {
        var options = _options.CurrentValue;

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(options.Subdomain))
        {
            return $"https://{options.Subdomain.Trim().Trim('/')}.chargify.com";
        }

        throw new MaxioConfigurationException(
            "Maxio is not configured. Set 'Maxio:ApiKey' and either 'Maxio:Subdomain' or 'Maxio:BaseUrl' " +
            "(for example via .NET user-secrets or environment configuration).");
    }

    private string CreateBasicCredentials()
    {
        var options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Set 'Maxio:ApiKey' (for example via .NET user-secrets or environment configuration).");
        }

        // Per the spec's security scheme the API key is the basic-auth username and "x" the password.
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(options.ApiKey + ":x"));
    }
}
