using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Typed HttpClient for the Maxio Advanced Billing REST API.
/// Base address and Basic-auth header are configured at registration time in Program.cs.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _httpClient = httpClient;
        settings.Value.Validate();
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // The product family can be addressed by its stable handle using the "handle:" prefix,
        // which keeps us independent of the numeric ids that change when the site is re-seeded.
        var responses = await _httpClient.GetFromJsonAsync<List<MaxioProductResponse>>(
            $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json",
            JsonOptions, cancellationToken);

        return (responses ?? new List<MaxioProductResponse>())
            .Select(r => r.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => p!)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var customerResponse = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions, cancellationToken);
        return customerResponse?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("customers.json", request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var customerResponse = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions, cancellationToken);
        return customerResponse?.Customer
            ?? throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty customer payload." });
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var subscriptionResponse = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(JsonOptions, cancellationToken);
        return subscriptionResponse?.Subscription
            ?? throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty subscription payload." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var responses = await _httpClient.GetFromJsonAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json", JsonOptions, cancellationToken);

        return (responses ?? new List<MaxioSubscriptionResponse>())
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        IReadOnlyList<string> errors = new[] { response.ReasonPhrase ?? "Unknown Maxio error" };
        try
        {
            var errorResponse = await response.Content.ReadFromJsonAsync<MaxioErrorResponse>(JsonOptions, cancellationToken);
            if (errorResponse?.Errors is { Count: > 0 })
            {
                errors = errorResponse.Errors;
            }
        }
        catch (JsonException)
        {
            // Body wasn't the expected {"errors":[...]} shape; keep the reason phrase.
        }

        throw new MaxioApiException(response.StatusCode, errors);
    }
}
