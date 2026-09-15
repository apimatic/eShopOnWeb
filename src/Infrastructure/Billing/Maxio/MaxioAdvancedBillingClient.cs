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
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// HTTP client for Maxio Advanced Billing. Auth is HTTP Basic with the API key as
/// the username and <c>x</c> as the password, as specified by the OpenAPI document.
/// </summary>
public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioAdvancedBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        ConfigureClient();
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();

        var products = new List<MaxioProduct>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            // GET /product_families/{product_family_id}/products.json
            // product_family_id: "Either the product family's id or its handle prefixed with `handle:`"
            var path =
                $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?page={page}&per_page={perPage}";
            var wrappers = await SendAsync<List<ProductResponse>>(HttpMethod.Get, path, null, cancellationToken)
                           ?? new List<ProductResponse>();
            var batch = wrappers
                .Select(wrapper => wrapper.Product)
                .Where(product => product is not null)
                .Cast<MaxioProduct>()
                .ToList();

            products.AddRange(batch);
            if (batch.Count < perPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();

        // GET /customers/lookup.json?reference=
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var wrapper = await SendAsync<CustomerResponse>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        CreateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();

        // POST /customers.json
        var wrapper = await SendAsync<CustomerResponse>(HttpMethod.Post, "customers.json", request, cancellationToken);
        if (wrapper?.Customer is null)
        {
            throw new BillingException("Maxio created a customer but returned an empty body.");
        }

        return wrapper.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();

        // GET /customers/{customer_id}/subscriptions.json
        var wrappers = await SendAsync<List<SubscriptionResponse>>(
                           HttpMethod.Get,
                           $"customers/{customerId}/subscriptions.json",
                           null,
                           cancellationToken)
                       ?? new List<SubscriptionResponse>();

        return wrappers
            .Select(wrapper => wrapper.Subscription)
            .Where(subscription => subscription is not null)
            .Cast<MaxioSubscription>()
            .ToList();
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();

        // GET /subscriptions/lookup.json?reference=
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var wrapper = await SendAsync<SubscriptionResponse>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return wrapper?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();

        // POST /subscriptions.json
        var wrapper = await SendAsync<SubscriptionResponse>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        if (wrapper?.Subscription is null)
        {
            throw new BillingException("Maxio created a subscription but returned an empty body.");
        }

        return wrapper.Subscription;
    }

    private void ConfigureClient()
    {
        if (_httpClient.BaseAddress is null && _options.IsConfigured)
        {
            _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl());
        }

        if (_httpClient.DefaultRequestHeaders.Authorization is null && !string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        if (_httpClient.DefaultRequestHeaders.Accept.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio/1.0");
        }
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, payload);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(payload, MaxioJson.SerializerOptions);
    }

    private static BillingException CreateApiException(HttpStatusCode statusCode, string payload)
    {
        var detail = TryReadErrorMessages(payload);
        var status = (int)statusCode;

        if (status == 401 || status == 403)
        {
            return new BillingConfigurationException(
                "Maxio rejected the API credentials. Check Maxio:ApiKey and Maxio:Subdomain.");
        }

        if (status == 404)
        {
            return new BillingException(detail ?? "The requested Maxio resource was not found.", 404);
        }

        if (status == 422)
        {
            return new SubscriptionEnrollmentException(detail ?? "Maxio could not process the billing request.");
        }

        return new BillingException(
            detail ?? $"Maxio Advanced Billing request failed with HTTP {status}.",
            status >= 500 ? 502 : status);
    }

    private static string? TryReadErrorMessages(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ErrorListResponse>(payload, MaxioJson.SerializerOptions);
            if (parsed?.Errors is { Count: > 0 })
            {
                return string.Join(" ", parsed.Errors);
            }
        }
        catch (JsonException)
        {
            // Fall through and return a truncated raw body.
        }

        var trimmed = payload.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }
}
