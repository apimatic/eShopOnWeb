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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        ValidateOptions();
        _httpClient.BaseAddress = _options.GetBaseAddress();
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var path = $"product_families/{Uri.EscapeDataString($"handle:{productFamilyHandle}")}/products.json?page=1&per_page=200";
        var products = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken);
        return products.Where(item => item.Product.ArchivedAt is null).Select(item => item.Product).ToArray();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            return (await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken)).Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken)
    {
        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };

        return (await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", body, cancellationToken)).Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            return (await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get, path, null, cancellationToken)).Subscription;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string subscriptionReference, CancellationToken cancellationToken)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                reference = subscriptionReference,
                payment_collection_method = "remittance"
            }
        };

        return (await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", body, cancellationToken)).Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var subscriptions = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);
        return subscriptions.Select(item => item.Subscription).ToArray();
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Maxio request {Method} {Path} failed with HTTP {StatusCode}.", method, path, (int)response.StatusCode);
            throw new MaxioApiException(response.StatusCode, responseBody);
        }

        var result = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
        return result ?? throw new InvalidOperationException("Maxio returned an empty JSON response.");
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            (string.IsNullOrWhiteSpace(_options.Subdomain) && string.IsNullOrWhiteSpace(_options.BaseUrl)) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio configuration requires ApiKey, ProductFamilyHandle, and either Subdomain or BaseUrl.");
        }
    }
}
