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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for Maxio Advanced Billing (formerly Chargify).
/// Auth: HTTP Basic, API key as username, literal password "x"
/// (Maxio "Core Resources for Building an Integration").
/// Paths verified against the official Maxio Advanced Billing .NET SDK (ab-dotnet-sdk 9.1.0):
/// GET  /product_families/{handle:FAMILY}/products.json
/// POST /customers.json
/// GET  /customers/lookup.json?reference=
/// GET  /customers/{id}/subscriptions.json
/// POST /subscriptions.json
/// GET  /subscriptions/lookup.json?reference=
/// </summary>
public class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioAdvancedBillingClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListProductsInFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            throw new BillingException((int)HttpStatusCode.InternalServerError,
                "Maxio:ProductFamilyHandle is not configured.");
        }

        // Product family id-or-handle: official docs accept the handle prefixed with "handle:".
        var familyKey = "handle:" + Uri.EscapeDataString(productFamilyHandle);
        var path = $"product_families/{familyKey}/products.json?per_page=200&include_archived=false";
        var envelopes = await GetAsync<List<MaxioProductEnvelope>>(path, cancellationToken) ?? new List<MaxioProductEnvelope>();

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && string.IsNullOrEmpty(p.ArchivedAt) && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => new SubscriptionPlan
            {
                Handle = p!.Handle!,
                Name = p.Name ?? p.Handle!,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? "month"
            })
            .ToList();
    }

    public async Task<MaxioCustomerRecord?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await GetOrNotFoundAsync<MaxioCustomerEnvelope>(path, cancellationToken);
        return MapCustomer(envelope?.Customer);
    }

    public async Task<MaxioCustomerRecord> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCustomerDto
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference,
                Organization = "eShopOnWeb"
            }
        };

        var envelope = await PostAsync<MaxioCustomerEnvelope>("customers.json", body, cancellationToken);
        var customer = MapCustomer(envelope.Customer);
        if (customer is null)
        {
            throw new MaxioApiException(500, "Maxio created a customer but returned an empty payload.");
        }

        return customer;
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(path, cancellationToken) ?? new List<MaxioSubscriptionEnvelope>();
        return envelopes
            .Select(e => MapSubscription(e.Subscription))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    public async Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await GetOrNotFoundAsync<MaxioSubscriptionEnvelope>(path, cancellationToken);
        return MapSubscription(envelope?.Subscription);
    }

    public async Task<ShopperSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string? reference, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionBody
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference
            }
        };

        var envelope = await PostAsync<MaxioSubscriptionEnvelope>("subscriptions.json", body, cancellationToken);
        var subscription = MapSubscription(envelope.Subscription);
        if (subscription is null)
        {
            throw new MaxioApiException(500, "Maxio created a subscription but returned an empty payload.");
        }

        return subscription;
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private async Task<T?> GetOrNotFoundAsync<T>(string relativeUrl, CancellationToken cancellationToken) where T : class
    {
        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private async Task<T> PostAsync<T>(string relativeUrl, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(relativeUrl, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(
            (int)response.StatusCode,
            $"Maxio Advanced Billing returned {(int)response.StatusCode} {response.ReasonPhrase}.",
            payload);
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var value = JsonSerializer.Deserialize<T>(json, JsonOptions);
        if (value is null)
        {
            throw new MaxioApiException(500, "Maxio Advanced Billing returned an empty JSON payload.");
        }

        return value;
    }

    private static MaxioCustomerRecord? MapCustomer(MaxioCustomerDto? dto)
    {
        if (dto is null || dto.Id <= 0)
        {
            return null;
        }

        return new MaxioCustomerRecord
        {
            Id = dto.Id,
            Email = dto.Email,
            Reference = dto.Reference
        };
    }

    private static ShopperSubscription? MapSubscription(MaxioSubscriptionDto? dto)
    {
        if (dto is null || dto.Id <= 0)
        {
            return null;
        }

        DateTimeOffset? nextBilling = null;
        if (!string.IsNullOrWhiteSpace(dto.NextAssessmentAt)
            && DateTimeOffset.TryParse(dto.NextAssessmentAt, out var parsed))
        {
            nextBilling = parsed;
        }

        var price = dto.ProductPriceInCents ?? dto.Product?.PriceInCents ?? 0;

        return new ShopperSubscription
        {
            Id = dto.Id,
            State = dto.State ?? string.Empty,
            ProductHandle = dto.Product?.Handle ?? string.Empty,
            ProductName = dto.Product?.Name ?? dto.Product?.Handle ?? string.Empty,
            PriceInCents = price,
            NextBillingAt = nextBilling,
            Reference = dto.Reference
        };
    }

    public static void ConfigureHttpClient(HttpClient client, MaxioOptions options)
    {
        var baseAddress = options.ResolveBaseAddress();
        if (!baseAddress.EndsWith('/'))
        {
            baseAddress += "/";
        }

        client.BaseAddress = new Uri(baseAddress, UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            // Confirmed: Basic auth username = API key, password = the literal character x.
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }
}
