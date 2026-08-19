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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioAdvancedBillingClient
{
    private const int MaxAttempts = 4;
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
        ConfigureHttpClient();
    }

    public string ProductFamilyHandle =>
        _options.ProductFamilyHandle
        ?? throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is required.");

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";
        var products = await SendAsync<List<MaxioProductResponse>>(HttpMethod.Get, path, null, cancellationToken);
        var result = new List<MaxioProduct>();
        if (products is null)
        {
            return result;
        }

        foreach (var item in products)
        {
            if (item.Product is not null)
            {
                result.Add(item.Product);
            }
        }

        return result;
    }

    public Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
        => SendOptionalAsync<MaxioCustomerResponse, MaxioCustomer>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            response => response?.Customer,
            cancellationToken);

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var created = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json", request, cancellationToken);
        if (created?.Customer is null)
        {
            throw new MaxioApiException("Maxio create customer returned an empty payload.", HttpStatusCode.BadGateway);
        }

        return created.Customer;
    }

    public Task<MaxioSubscription?> LookupSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
        => SendOptionalAsync<MaxioSubscriptionResponse, MaxioSubscription>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            response => response?.Subscription,
            cancellationToken);

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var payload = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);

        var result = new List<MaxioSubscription>();
        if (payload is null)
        {
            return result;
        }

        foreach (var item in payload)
        {
            if (item.Subscription is not null)
            {
                result.Add(item.Subscription);
            }
        }

        return result;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var created = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        if (created?.Subscription is null)
        {
            throw new MaxioApiException("Maxio create subscription returned an empty payload.", HttpStatusCode.BadGateway);
        }

        return created.Subscription;
    }

    private void ConfigureHttpClient()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new MaxioConfigurationException("Maxio:ApiKey is required.");
        }

        _httpClient.BaseAddress = _options.ResolveBaseAddress();
        _httpClient.Timeout = TimeSpan.FromSeconds(100);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private async Task<T?> SendOptionalAsync<TResponse, T>(
        HttpMethod method,
        string path,
        Func<TResponse?, T?> selector,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var payload = await SendAsync<TResponse>(method, path, null, cancellationToken);
            return selector(payload);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var request = CreateRequest(method, path, body);
            response = await _httpClient.SendAsync(request, cancellationToken);

            if ((int)response.StatusCode != 429 || attempt == MaxAttempts)
            {
                break;
            }

            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
            _logger.LogWarning("Maxio returned 429 for {Path}; retrying in {Delay}.", path, delay);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }

        using (response)
        {
            var content = response is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);
            if (response is null)
            {
                throw new MaxioApiException("No response from Maxio.", HttpStatusCode.BadGateway);
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                throw new MaxioDuplicateSubmissionException();
            }

            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadErrors(content) ?? $"Maxio request failed with {(int)response.StatusCode}.";
                throw new MaxioApiException(message, response.StatusCode, content);
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(content, MaxioJson.SerializerOptions);
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: MaxioJson.SerializerOptions);
        }

        return request;
    }

    private static string? TryReadErrors(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            var errors = JsonSerializer.Deserialize<MaxioErrorResponse>(content, MaxioJson.SerializerOptions);
            if (errors?.Errors is { Count: > 0 })
            {
                return string.Join(" ", errors.Errors);
            }
        }
        catch (JsonException)
        {
            // fall through to raw body
        }

        return content.Length > 500 ? content[..500] : content;
    }
}
