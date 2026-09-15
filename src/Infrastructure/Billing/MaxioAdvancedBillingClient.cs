using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// HTTP client for Maxio Advanced Billing (formerly Chargify). Paths and
/// payloads match the official Advanced Billing API:
/// https://github.com/maxio-com/ab-dotnet-sdk
/// </summary>
public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(HttpClient httpClient, ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            var familyId = $"handle:{productFamilyHandle}";
            var path = $"product_families/{familyId}/products.json?page={page}&per_page={perPage}&include_archived=false";
            var wrappers = await GetAsync<List<MaxioProductResponse>>(path, cancellationToken);
            if (wrappers is null || wrappers.Count == 0)
            {
                break;
            }

            foreach (var wrapper in wrappers)
            {
                if (wrapper.Product is not null)
                {
                    products.Add(wrapper.Product);
                }
            }

            if (wrappers.Count < perPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        var path = $"products/handle/{productHandle}.json";
        var wrapper = await GetAsync<MaxioProductResponse>(path, cancellationToken, allowNotFound: true);
        return wrapper?.Product;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var wrapper = await GetAsync<MaxioCustomerResponse>(path, cancellationToken, allowNotFound: true);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var wrapper = await SendJsonAsync<MaxioCreateCustomerRequest, MaxioCustomerResponse>(
            HttpMethod.Post,
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            cancellationToken);

        return wrapper.Customer ?? throw new BillingException("Maxio created a customer but returned an empty body.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var wrappers = await GetAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken);

        var subscriptions = new List<MaxioSubscription>();
        if (wrappers is null)
        {
            return subscriptions;
        }

        foreach (var wrapper in wrappers)
        {
            if (wrapper.Subscription is not null)
            {
                subscriptions.Add(wrapper.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var wrapper = await GetAsync<MaxioSubscriptionResponse>(path, cancellationToken, allowNotFound: true);
        return wrapper?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var wrapper = await SendJsonAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>(
            HttpMethod.Post,
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription },
            cancellationToken);

        return wrapper.Subscription ?? throw new BillingException("Maxio created a subscription but returned an empty body.");
    }

    private async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var response = await _httpClient.GetAsync(relativePath, cancellationToken);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(MaxioJson.Options, cancellationToken);
    }

    private async Task<TResponse> SendJsonAsync<TRequest, TResponse>(
        HttpMethod method,
        string relativePath,
        TRequest body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativePath)
        {
            Content = JsonContent.Create(body, options: MaxioJson.Options)
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<TResponse>(MaxioJson.Options, cancellationToken);
        if (payload is null)
        {
            throw new BillingException($"Maxio returned an empty {typeof(TResponse).Name} body.");
        }

        return payload;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = TryFormatMaxioError(body) ?? $"Maxio request failed with {(int)response.StatusCode} {response.ReasonPhrase}.";

        _logger.LogWarning("Maxio API {Status} for {Method} {Uri}: {Body}",
            (int)response.StatusCode, response.RequestMessage?.Method, response.RequestMessage?.RequestUri, body);

        var status = response.StatusCode switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict => 400,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => 502,
            HttpStatusCode.NotFound => 404,
            _ => 502
        };

        throw new BillingException(message, status);
    }

    private static string? TryFormatMaxioError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    var builder = new StringBuilder();
                    foreach (var item in errors.EnumerateArray())
                    {
                        if (builder.Length > 0)
                        {
                            builder.Append(' ');
                        }

                        builder.Append(item.ToString());
                    }

                    if (builder.Length > 0)
                    {
                        return builder.ToString();
                    }
                }
                else if (errors.ValueKind == JsonValueKind.Object)
                {
                    return errors.ToString();
                }
                else if (errors.ValueKind == JsonValueKind.String)
                {
                    return errors.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw body.
        }

        return body.Length > 500 ? body[..500] : body;
    }
}
