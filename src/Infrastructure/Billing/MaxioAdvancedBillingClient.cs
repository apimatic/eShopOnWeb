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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Models;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// HTTP client for Maxio Advanced Billing, built against the OpenAPI contract in maxio-spec/.
/// Auth is HTTP Basic with the API key as username and <c>x</c> as password.
/// </summary>
public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private const int MaxPageSize = 200;
    private readonly HttpClient _httpClient;

    public MaxioAdvancedBillingClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<CustomerResponse>(HttpMethod.Get, path, body: null, allowNotFound: true, cancellationToken);
        return response?.Customer;
    }

    public async Task<Customer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<CustomerResponse>(
            HttpMethod.Post,
            "customers.json",
            new CreateCustomerRequest { Customer = customer },
            allowNotFound: false,
            cancellationToken);

        return Require(response?.Customer, "Create Customer returned no customer.");
    }

    public async Task<IReadOnlyList<Product>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var familyId = $"handle:{productFamilyHandle}";
        var products = new List<Product>();
        var page = 1;

        while (true)
        {
            var path = $"product_families/{familyId}/products.json?page={page}&per_page={MaxPageSize}&include_archived=false";
            var pageItems = await SendAsync<List<ProductResponse>>(HttpMethod.Get, path, body: null, allowNotFound: false, cancellationToken)
                            ?? new List<ProductResponse>();

            products.AddRange(pageItems
                .Select(item => item.Product)
                .Where(product => product is not null && product.ArchivedAt is null)
                .Cast<Product>());

            if (pageItems.Count < MaxPageSize)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<SubscriptionResponse>(HttpMethod.Get, path, body: null, allowNotFound: true, cancellationToken);
        return response?.Subscription;
    }

    public async Task<Subscription> CreateSubscriptionAsync(CreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<SubscriptionResponse>(
            HttpMethod.Post,
            "subscriptions.json",
            new CreateSubscriptionRequest { Subscription = subscription },
            allowNotFound: false,
            cancellationToken);

        return Require(response?.Subscription, "Create Subscription returned no subscription.");
    }

    public async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var items = await SendAsync<List<SubscriptionResponse>>(HttpMethod.Get, path, body: null, allowNotFound: false, cancellationToken)
                    ?? new List<SubscriptionResponse>();

        return items
            .Select(item => item.Subscription)
            .Where(subscription => subscription is not null)
            .Cast<Subscription>()
            .ToList();
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativeUrl,
        object? body,
        bool allowNotFound,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        HttpResponseMessage? response = null;
        string content = string.Empty;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativeUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            response = await _httpClient.SendAsync(request, cancellationToken);
            content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (IsTransient(response.StatusCode) && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                continue;
            }

            break;
        }

        if (response is null)
        {
            throw new MaxioApiException(0, "No response received from Maxio Advanced Billing.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(content, MaxioJson.SerializerOptions);
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static MaxioApiException CreateApiException(HttpStatusCode statusCode, string content)
    {
        var errors = ParseErrors(content);
        var message = errors.Count > 0
            ? string.Join(" ", errors)
            : (string.IsNullOrWhiteSpace(content)
                ? $"Maxio Advanced Billing request failed with status {(int)statusCode}."
                : content.Trim());

        return new MaxioApiException((int)statusCode, message, errors);
    }

    private static IReadOnlyList<string> ParseErrors(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<string>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ErrorListResponse>(content, MaxioJson.SerializerOptions);
            return parsed?.Errors?.Where(error => !string.IsNullOrWhiteSpace(error)).ToList()
                   ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static T Require<T>(T? value, string message) where T : class
    {
        if (value is null)
        {
            throw new MaxioApiException((int)HttpStatusCode.BadGateway, message);
        }

        return value;
    }
}
