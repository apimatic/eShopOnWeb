using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for Maxio Advanced Billing. Paths, query parameters, and payloads follow
/// the OpenAPI specification in maxio-spec/.
/// </summary>
public class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private const int DefaultPageSize = 200;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;
    private readonly MaxioOptions _options;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productFamilyHandle);

        var plans = new List<SubscriptionPlan>();
        var page = 1;

        while (true)
        {
            // GET /product_families/{product_family_id}/products.json
            // product_family_id may be the family's handle prefixed with `handle:`
            var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?page={page}&per_page={DefaultPageSize}";
            var wrapped = await GetJsonAsync<List<ProductResponse>>(path, cancellationToken) ?? new List<ProductResponse>();

            foreach (var item in wrapped)
            {
                if (item.Product is null || item.Product.ArchivedAt is not null)
                {
                    continue;
                }

                plans.Add(MaxioMapping.ToPlan(item.Product));
            }

            if (wrapped.Count < DefaultPageSize)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<SubscriptionPlan?> ReadProductByHandleAsync(
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productHandle);

        // GET /products/handle/{api_handle}.json
        var path = $"products/handle/{Uri.EscapeDataString(productHandle)}.json";
        var wrapped = await GetJsonAsync<ProductResponse>(path, cancellationToken, treatNotFoundAsNull: true);
        return wrapped?.Product is null ? null : MaxioMapping.ToPlan(wrapped.Product);
    }

    public async Task<BillingCustomer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        // GET /customers/lookup.json?reference=
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var wrapped = await GetJsonAsync<CustomerResponse>(path, cancellationToken, treatNotFoundAsNull: true);
        return wrapped?.Customer is null ? null : MaxioMapping.ToCustomer(wrapped.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string reference,
        CancellationToken cancellationToken = default)
    {
        var payload = new CreateCustomerRequest
        {
            Customer = new CreateCustomerDto
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        // POST /customers.json
        var wrapped = await PostJsonAsync<CreateCustomerRequest, CustomerResponse>("customers.json", payload, cancellationToken);
        if (wrapped?.Customer is null)
        {
            throw new MaxioApiException(502, "Maxio createCustomer returned an empty customer payload.");
        }

        return MaxioMapping.ToCustomer(wrapped.Customer);
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        // GET /customers/{customer_id}/subscriptions.json
        var path = $"customers/{customerId}/subscriptions.json";
        var wrapped = await GetJsonAsync<List<SubscriptionResponse>>(path, cancellationToken) ?? new List<SubscriptionResponse>();
        var subscriptions = new List<ShopperSubscription>(wrapped.Count);
        foreach (var item in wrapped)
        {
            if (item.Subscription is not null)
            {
                subscriptions.Add(MaxioMapping.ToSubscription(item.Subscription));
            }
        }

        return subscriptions;
    }

    public async Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        // GET /subscriptions/lookup.json?reference=
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var wrapped = await GetJsonAsync<SubscriptionResponse>(path, cancellationToken, treatNotFoundAsNull: true);
        return wrapped?.Subscription is null ? null : MaxioMapping.ToSubscription(wrapped.Subscription);
    }

    public async Task<ShopperSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string reference,
        string paymentCollectionMethod,
        CancellationToken cancellationToken = default)
    {
        var payload = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionDto
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference,
                PaymentCollectionMethod = paymentCollectionMethod
            }
        };

        // POST /subscriptions.json
        var wrapped = await PostJsonAsync<CreateSubscriptionRequest, SubscriptionResponse>("subscriptions.json", payload, cancellationToken);
        if (wrapped?.Subscription is null)
        {
            throw new MaxioApiException(502, "Maxio createSubscription returned an empty subscription payload.");
        }

        return MaxioMapping.ToSubscription(wrapped.Subscription);
    }

    private async Task<T?> GetJsonAsync<T>(string relativePath, CancellationToken cancellationToken, bool treatNotFoundAsNull = false)
    {
        EnsureReady();
        using var response = await _httpClient.GetAsync(relativePath, cancellationToken);
        return await ReadJsonAsync<T>(response, relativePath, treatNotFoundAsNull, cancellationToken);
    }

    private async Task<TResponse?> PostJsonAsync<TRequest, TResponse>(string relativePath, TRequest body, CancellationToken cancellationToken)
    {
        EnsureReady();
        using var response = await _httpClient.PostAsJsonAsync(relativePath, body, MaxioJson.SerializerOptions, cancellationToken);
        return await ReadJsonAsync<TResponse>(response, relativePath, treatNotFoundAsNull: false, cancellationToken);
    }

    private async Task<T?> ReadJsonAsync<T>(
        HttpResponseMessage response,
        string relativePath,
        bool treatNotFoundAsNull,
        CancellationToken cancellationToken)
    {
        if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = MaxioErrorFormatter.Format(errorBody);
            _logger.LogWarning("Maxio {Method} {Path} failed with {StatusCode}: {Message}",
                response.RequestMessage?.Method, relativePath, (int)response.StatusCode, message);
            throw new MaxioApiException((int)response.StatusCode, message);
        }

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(MaxioJson.SerializerOptions, cancellationToken);
    }

    private void EnsureReady()
    {
        try
        {
            _options.EnsureConfigured();
        }
        catch (InvalidOperationException ex)
        {
            throw new BillingConfigurationException(ex.Message);
        }
    }
}
