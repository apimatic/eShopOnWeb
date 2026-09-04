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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

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
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var path = $"product_families/{Uri.EscapeDataString("handle:" + productFamilyHandle)}/products.json?page=1&per_page=200";
        var response = await SendAsync<List<MaxioProductResponse>>(HttpMethod.Get, path, null, cancellationToken);
        var products = new List<MaxioProduct>(response.Count);
        foreach (var item in response)
        {
            if (item.Product is not null)
                products.Add(item.Product);
        }

        return products;
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Get, path, null, cancellationToken);
            return response.Customer;
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
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

        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json", body, cancellationToken);
        return response.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var response = await SendAsync<List<MaxioSubscriptionResponse>>(HttpMethod.Get, path, null, cancellationToken);
        var subscriptions = new List<MaxioSubscription>(response.Count);
        foreach (var item in response)
        {
            if (item.Subscription is not null)
                subscriptions.Add(item.Subscription);
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string reference, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                reference,
                payment_collection_method = "remittance"
            },
            uniqueness_token = uniquenessToken
        };

        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        return response.Subscription;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Keep the upstream body out of the customer-facing response; it can contain
            // account-specific data. The status and body are available to server logs.
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                _logger.LogDebug("Maxio Billing API request to {Path} returned not found", path);
            else
                _logger.LogError("Maxio Billing API request to {Path} failed with status {StatusCode}", path, response.StatusCode);
            throw new MaxioApiException(response.StatusCode, errorBody);
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response.");
    }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string responseBody)
        : base($"Maxio Billing API returned {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string ResponseBody { get; }
}
