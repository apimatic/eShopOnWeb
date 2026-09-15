using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for Maxio Advanced Billing, shaped exclusively by <c>maxio-spec/openapi.yaml</c>:
/// Basic auth (API key as username, password <c>x</c>), JSON, and the documented paths.
/// </summary>
internal sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Product>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        // GET /product_families/{product_family_id}/products.json
        // product_family_id: "Either the product family's id or its handle prefixed with `handle:`"
        var familySegment = Uri.EscapeDataString($"handle:{productFamilyHandle}");
        var products = new List<Product>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            var path = $"product_families/{familySegment}/products.json?page={page}&per_page={perPage}";
            var pageItems = await GetAsync<List<ProductResponse>>(path, cancellationToken) ?? new List<ProductResponse>();
            foreach (var wrapper in pageItems)
            {
                if (wrapper.Product is not null)
                {
                    products.Add(wrapper.Product);
                }
            }

            if (pageItems.Count < perPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        // GET /customers/lookup.json?reference=
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var response = await GetAsync<CustomerResponse>(path, cancellationToken);
            return response?.Customer;
        }
        catch (BillingException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Customer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken)
    {
        // POST /customers.json
        var response = await PostAsync<CreateCustomerRequest, CustomerResponse>(
            "customers.json",
            new CreateCustomerRequest { Customer = customer },
            cancellationToken);

        if (response?.Customer?.Id is null)
        {
            throw new BillingException(502, "Maxio createCustomer returned no customer.");
        }

        return response.Customer;
    }

    public async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        // GET /customers/{customer_id}/subscriptions.json
        var path = $"customers/{customerId}/subscriptions.json";
        var wrappers = await GetAsync<List<SubscriptionResponse>>(path, cancellationToken) ?? new List<SubscriptionResponse>();
        var subscriptions = new List<Subscription>();
        foreach (var wrapper in wrappers)
        {
            if (wrapper.Subscription is not null)
            {
                subscriptions.Add(wrapper.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        // GET /subscriptions/lookup.json?reference=
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var response = await GetAsync<SubscriptionResponse>(path, cancellationToken);
            return response?.Subscription;
        }
        catch (BillingException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Subscription> CreateSubscriptionAsync(CreateSubscription subscription, CancellationToken cancellationToken)
    {
        // POST /subscriptions.json
        var response = await PostAsync<CreateSubscriptionRequest, SubscriptionResponse>(
            "subscriptions.json",
            new CreateSubscriptionRequest { Subscription = subscription },
            cancellationToken);

        if (response?.Subscription?.Id is null)
        {
            throw new BillingException(502, "Maxio createSubscription returned no subscription.");
        }

        return response.Subscription;
    }

    private async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        return await SendAsync<T>(request, cancellationToken);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string relativePath,
        TRequest body,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        return await SendAsync<TResponse>(request, cancellationToken);
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Maxio HTTP request to {Path} failed", request.RequestUri);
            throw new BillingException(502, "Unable to reach Maxio Advanced Billing.", ex);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(payload, MaxioJson.SerializerOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize Maxio response for {Path}", request.RequestUri);
                throw new BillingException(502, "Maxio returned a response that could not be read.", ex);
            }
        }

        var message = FormatError(response.StatusCode, payload);
        var status = (int)response.StatusCode;
        if (status == (int)HttpStatusCode.NotFound)
        {
            throw new BillingException(404, message);
        }

        if (status == 422)
        {
            throw new BillingException(422, message);
        }

        if (status == (int)HttpStatusCode.Unauthorized || status == (int)HttpStatusCode.Forbidden)
        {
            throw new BillingException(502, "Maxio rejected the API credentials.");
        }

        _logger.LogWarning("Maxio returned {Status} for {Path}: {Body}", status, request.RequestUri, Truncate(payload));
        throw new BillingException(502, message);
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new BillingException(503, "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.");
        }
    }

    private static string FormatError(HttpStatusCode statusCode, string payload)
    {
        var errors = TryReadErrors(payload);
        if (errors.Length > 0)
        {
            return $"Maxio Advanced Billing error ({(int)statusCode}): {string.Join(" ", errors)}";
        }

        if (!string.IsNullOrWhiteSpace(payload))
        {
            return $"Maxio Advanced Billing error ({(int)statusCode}): {Truncate(payload)}";
        }

        return $"Maxio Advanced Billing error ({(int)statusCode}).";
    }

    private static string[] TryReadErrors(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<string>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ErrorResponse>(payload, MaxioJson.SerializerOptions);
            return parsed?.Errors ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string Truncate(string value, int max = 400)
        => value.Length <= max ? value : value[..max];
}
