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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var maxioOptions = options.Value;
        if (_httpClient.BaseAddress is null && maxioOptions.IsConfigured)
        {
            _httpClient.BaseAddress = maxioOptions.GetApiBaseAddress();
        }

        if (_httpClient.DefaultRequestHeaders.Authorization is null
            && !string.IsNullOrWhiteSpace(maxioOptions.ApiKey))
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{maxioOptions.ApiKey}:x"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        if (_httpClient.DefaultRequestHeaders.Accept.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    public async Task<IReadOnlyList<ProductPayload>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(productFamilyHandle, cancellationToken);
        var relativeUrl = $"product_families/{familyId}/products.json?per_page=200";
        var products = await SendAsync<List<ProductResponse>>(HttpMethod.Get, relativeUrl, null, cancellationToken);
        return products?
            .Select(wrapper => wrapper.Product)
            .Where(product => product is not null)
            .Cast<ProductPayload>()
            .ToList()
            ?? (IReadOnlyList<ProductPayload>)Array.Empty<ProductPayload>();
    }

    private async Task<int> ResolveProductFamilyIdAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var families = await SendAsync<List<ProductFamilyResponse>>(
            HttpMethod.Get,
            "product_families.json",
            null,
            cancellationToken);

        var match = families?
            .Select(wrapper => wrapper.ProductFamily)
            .FirstOrDefault(family =>
                family?.Id is not null
                && string.Equals(family.Handle, productFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is null)
        {
            throw new MaxioApiException(
                HttpStatusCode.NotFound,
                $"Product family '{productFamilyHandle}' was not found on the Maxio site.");
        }

        return match.Id.Value;
    }

    public async Task<ProductPayload?> ReadProductByHandleAsync(
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        var relativeUrl = $"products/handle/{Uri.EscapeDataString(productHandle)}.json";
        var response = await SendAsync<ProductResponse>(
            HttpMethod.Get,
            relativeUrl,
            null,
            cancellationToken,
            HttpStatusCode.NotFound);
        return response?.Product;
    }

    public async Task<CustomerPayload?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var relativeUrl = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<CustomerResponse>(
            HttpMethod.Get,
            relativeUrl,
            null,
            cancellationToken,
            HttpStatusCode.NotFound);
        return response?.Customer;
    }

    public async Task<CustomerPayload> CreateCustomerAsync(
        CreateCustomerPayload customer,
        CancellationToken cancellationToken = default)
    {
        var created = await SendAsync<CustomerResponse>(
            HttpMethod.Post,
            "customers.json",
            new CreateCustomerRequest { Customer = customer },
            cancellationToken);

        if (created?.Customer is null || created.Customer.Id is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio did not return a customer.");
        }

        return created.Customer;
    }

    public async Task<IReadOnlyList<SubscriptionPayload>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var relativeUrl = $"customers/{customerId}/subscriptions.json";
        var subscriptions = await SendAsync<List<SubscriptionResponse>>(
            HttpMethod.Get,
            relativeUrl,
            null,
            cancellationToken);
        return subscriptions?
            .Select(wrapper => wrapper.Subscription)
            .Where(subscription => subscription is not null)
            .Cast<SubscriptionPayload>()
            .ToList()
            ?? (IReadOnlyList<SubscriptionPayload>)Array.Empty<SubscriptionPayload>();
    }

    public async Task<SubscriptionPayload> CreateSubscriptionAsync(
        CreateSubscriptionPayload subscription,
        CancellationToken cancellationToken = default)
    {
        var created = await SendAsync<SubscriptionResponse>(
            HttpMethod.Post,
            "subscriptions.json",
            new CreateSubscriptionRequest { Subscription = subscription },
            cancellationToken);

        if (created?.Subscription is null || created.Subscription.Id is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio did not return a subscription.");
        }

        return created.Subscription;
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken,
        params HttpStatusCode[] allowedStatuses)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, MaxioJson.Options);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("Maxio {Method} {Url}", method, relativeUrl);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(payload, MaxioJson.Options);
        }

        if (allowedStatuses.Contains(response.StatusCode))
        {
            return default;
        }

        var message = FormatError(response.StatusCode, payload);
        _logger.LogWarning("Maxio {Method} {Url} failed with {Status}: {Message}", method, relativeUrl, (int)response.StatusCode, message);
        throw new MaxioApiException(response.StatusCode, message);
    }

    private static string FormatError(HttpStatusCode statusCode, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return $"Maxio API returned {(int)statusCode} {statusCode}.";
        }

        try
        {
            var error = JsonSerializer.Deserialize<ErrorListResponse>(payload, MaxioJson.Options);
            if (error?.Errors.ValueKind == JsonValueKind.Array)
            {
                var messages = error.Errors.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                    .Where(item => !string.IsNullOrWhiteSpace(item));
                var joined = string.Join(" ", messages!);
                if (!string.IsNullOrWhiteSpace(joined))
                {
                    return joined;
                }
            }
            else if (error?.Errors.ValueKind == JsonValueKind.Object)
            {
                return error.Errors.ToString();
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw payload.
        }

        return $"Maxio API returned {(int)statusCode} {statusCode}: {payload}";
    }
}
