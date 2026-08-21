using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private const int ProductsPerPage = 200;

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

    public string ProductFamilyHandle
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
            {
                throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is not configured.");
            }

            return _options.ProductFamilyHandle.Trim();
        }
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListProductsForProductFamilyAsync(CancellationToken cancellationToken = default)
    {
        // Spec: GET /product_families/{product_family_id}/products.json
        // product_family_id is the id or handle prefixed with `handle:`
        var familyKey = $"handle:{ProductFamilyHandle}";
        var plans = new List<SubscriptionPlan>();
        var page = 1;

        while (true)
        {
            var path = $"product_families/{familyKey}/products.json?page={page}&per_page={ProductsPerPage}";
            var batch = await SendAsync<List<MaxioContracts.ProductResponse>>(HttpMethod.Get, path, body: null, cancellationToken)
                        ?? new List<MaxioContracts.ProductResponse>();

            foreach (var wrapper in batch)
            {
                if (wrapper.Product is null || wrapper.Product.ArchivedAt is not null)
                {
                    continue;
                }

                plans.Add(MaxioContracts.ToPlan(wrapper.Product));
            }

            if (batch.Count < ProductsPerPage)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<SubscriptionPlan?> ReadProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        // Spec: GET /products/handle/{api_handle}.json
        var path = $"products/handle/{Uri.EscapeDataString(productHandle)}.json";
        var response = await SendAsync<MaxioContracts.ProductResponse>(HttpMethod.Get, path, body: null, cancellationToken, treatNotFoundAsNull: true);
        if (response?.Product is null || response.Product.ArchivedAt is not null)
        {
            return null;
        }

        return MaxioContracts.ToPlan(response.Product);
    }

    public async Task<BillingCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        // Spec: GET /customers/lookup.json?reference=
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<MaxioContracts.CustomerResponse>(HttpMethod.Get, path, body: null, cancellationToken, treatNotFoundAsNull: true);
        return response?.Customer is null ? null : MaxioContracts.ToCustomer(response.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
        // Spec: POST /customers.json
        var body = new MaxioContracts.CreateCustomerRequest
        {
            Customer = new MaxioContracts.CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var response = await SendAsync<MaxioContracts.CustomerResponse>(HttpMethod.Post, "customers.json", body, cancellationToken);
        if (response?.Customer is null)
        {
            throw new MaxioApiException("Maxio createCustomer returned an empty customer.", 502);
        }

        return MaxioContracts.ToCustomer(response.Customer);
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        // Spec: GET /customers/{customer_id}/subscriptions.json
        var path = $"customers/{customerId}/subscriptions.json";
        var batch = await SendAsync<List<MaxioContracts.SubscriptionResponse>>(HttpMethod.Get, path, body: null, cancellationToken)
                    ?? new List<MaxioContracts.SubscriptionResponse>();

        return batch
            .Where(wrapper => wrapper.Subscription is not null)
            .Select(wrapper => MaxioContracts.ToShopperSubscription(wrapper.Subscription!))
            .ToList();
    }

    public async Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        // Spec: GET /subscriptions/lookup.json?reference=
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<MaxioContracts.SubscriptionResponse>(HttpMethod.Get, path, body: null, cancellationToken, treatNotFoundAsNull: true);
        return response?.Subscription is null ? null : MaxioContracts.ToShopperSubscription(response.Subscription);
    }

    public async Task<ShopperSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string reference,
        string paymentCollectionMethod,
        CancellationToken cancellationToken = default)
    {
        // Spec: POST /subscriptions.json
        var body = new MaxioContracts.CreateSubscriptionRequest
        {
            Subscription = new MaxioContracts.CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference,
                PaymentCollectionMethod = paymentCollectionMethod
            }
        };

        var response = await SendAsync<MaxioContracts.SubscriptionResponse>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        if (response?.Subscription is null)
        {
            throw new MaxioApiException("Maxio createSubscription returned an empty subscription.", 502);
        }

        return MaxioContracts.ToShopperSubscription(response.Subscription);
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        bool treatNotFoundAsNull = false)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, mediaType: MaxioJson.JsonMediaType, options: MaxioJson.Options);
        }

        _logger.LogInformation("Maxio {Method} {Path}", method.Method, relativePath);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound && treatNotFoundAsNull)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Maxio {Method} {Path} failed with {StatusCode}.", method.Method, relativePath, (int)response.StatusCode);
            throw MaxioContracts.ToApiException((int)response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content) || content == "null")
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, MaxioJson.Options);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException($"Maxio returned a response that could not be parsed: {ex.Message}", (int)response.StatusCode, content);
        }
    }
}
