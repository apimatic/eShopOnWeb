using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Narrow HTTP client for the Billing API operations used by subscriptions.
/// The request shapes follow the Maxio Billing API reference.
/// </summary>
public sealed class MaxioBillingClient
{
    private const int PageSize = 200;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MaxioSettings _settings;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public MaxioBillingClient(IHttpClientFactory httpClientFactory, IOptions<MaxioSettings> options)
    {
        _httpClientFactory = httpClientFactory;
        _settings = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var plans = new List<MaxioProduct>();
        for (var page = 1; ; page++)
        {
            var handle = Uri.EscapeDataString(_settings.ProductFamilyHandle);
            var pageItems = await GetAsync<List<MaxioProductEnvelope>>(
                $"product_families/handle:{handle}/products.json?page={page}&per_page={PageSize}", cancellationToken);

            plans.AddRange(pageItems.Select(item => item.Product)
                .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null));

            if (pageItems.Count < PageSize)
            {
                return plans;
            }
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer ?? throw new InvalidOperationException("Maxio returned an invalid customer response.");
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, string uniquenessToken,
        CancellationToken cancellationToken)
    {
        var envelope = await SendForJsonAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json",
            new { customer, uniqueness_token = uniquenessToken }, cancellationToken);
        return envelope.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId,
        CancellationToken cancellationToken)
    {
        var subscriptions = new List<MaxioSubscription>();
        for (var page = 1; ; page++)
        {
            var pageItems = await GetAsync<List<MaxioSubscriptionEnvelope>>(
                $"customers/{customerId}/subscriptions.json?page={page}&per_page={PageSize}", cancellationToken);
            subscriptions.AddRange(pageItems.Select(item => item.Subscription));
            if (pageItems.Count < PageSize)
            {
                return subscriptions;
            }
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle,
        string uniquenessToken, CancellationToken cancellationToken)
    {
        var envelope = await SendForJsonAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json",
            new
            {
                // The demo plans do not require a payment method. Invoice collection lets Maxio
                // create the recurring subscription without capturing card data or invoking 3DS.
                subscription = new { product_handle = productHandle, customer_id = customerId, payment_collection_method = "invoice" },
                uniqueness_token = uniquenessToken
            },
            cancellationToken);
        return envelope.Subscription;
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, relativeUrl, null, cancellationToken);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Maxio returned an empty response.");
    }

    private async Task<T> SendForJsonAsync<T>(HttpMethod method, string relativeUrl, object body,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, relativeUrl, body, cancellationToken);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Maxio returned an empty response.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUrl, object? body,
        CancellationToken cancellationToken)
    {
        _settings.EnsureConfigured();
        var client = _httpClientFactory.CreateClient(nameof(MaxioBillingClient));
        client.BaseAddress = _settings.GetBaseUri();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ApiKey}:X")));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    private static Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return Task.CompletedTask;
        }

        throw new MaxioApiException(response.StatusCode);
    }
}

public sealed class MaxioCustomerCreate
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; init; } = string.Empty;
    [JsonPropertyName("last_name")]
    public string LastName { get; init; } = string.Empty;
    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;
    [JsonPropertyName("reference")]
    public string Reference { get; init; } = string.Empty;
}

public sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer Customer { get; init; } = new();
}

public sealed class MaxioCustomer
{
    public int Id { get; init; }
    public string? Reference { get; init; }
}

public sealed class MaxioProductEnvelope
{
    public MaxioProduct Product { get; init; } = new();
}

public sealed class MaxioProduct
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Handle { get; init; }
    [JsonPropertyName("description")]
    public string? Description { get; init; }
    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; init; } = string.Empty;
    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; init; }
}

public sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription Subscription { get; init; } = new();
}

public sealed class MaxioSubscription
{
    public long Id { get; init; }
    public string State { get; init; } = string.Empty;
    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; init; }
    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public MaxioProduct? Product { get; init; }
}
