using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing (formerly Chargify) API.
/// JSON endpoints, Basic authentication with the API key as username.
/// See https://developers.maxio.com (Advanced Billing API reference).
/// </summary>
public class MaxioBillingClient : IMaxioBillingClient
{
    private const string CollectionMethodInvoice = "invoice";
    private const int MaxPageSize = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(_options.EffectiveBaseUrl);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await GetListAsync<ProductResponse>(
            $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json",
            cancellationToken);

        return products
            .Select(p => p.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => new SubscriptionPlan(
                p!.Id,
                p.Handle ?? string.Empty,
                p.Name ?? string.Empty,
                p.Description,
                p.PriceInCents,
                p.Interval,
                p.IntervalUnit ?? string.Empty,
                p.RequireCreditCard == true))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, content);

        var customer = Deserialize<CustomerResponse>(content)?.Customer;
        return customer is null ? null : customer.ToCustomer();
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string? lastName,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };

        var customer = await SendForSingleAsync<CustomerResponse>("customers.json", body, cancellationToken);
        return customer.Customer!.ToCustomer();
    }

    public async Task<MaxioSubscriptionInfo> CreateSubscriptionAsync(int billingCustomerId, string planHandle,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = planHandle,
                customer_id = billingCustomerId,
                // Relationship Invoicing: no payment method is captured; the billing
                // system invoices the customer instead of charging a stored card.
                payment_collection_method = CollectionMethodInvoice
            }
        };

        var subscription = await SendForSingleAsync<SubscriptionResponse>("subscriptions.json", body, cancellationToken);
        return subscription.Subscription!.ToSubscriptionInfo();
    }

    public async Task<IReadOnlyList<MaxioSubscriptionInfo>> ListCustomerSubscriptionsAsync(int billingCustomerId,
        CancellationToken cancellationToken = default)
    {
        var result = new List<MaxioSubscriptionInfo>();
        var page = 1;
        while (true)
        {
            var batch = await GetListAsync<SubscriptionResponse>(
                $"subscriptions.json?customer_id={billingCustomerId}&per_page={MaxPageSize}&page={page}&sort=created_at&direction=desc",
                cancellationToken);

            result.AddRange(batch.Select(s => s.Subscription).Where(s => s is not null).Select(s => s!.ToSubscriptionInfo()));

            if (batch.Count < MaxPageSize)
            {
                return result;
            }

            page++;
        }
    }

    private async Task<List<T>> GetListAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, content);
        return Deserialize<List<T>>(content) ?? new List<T>();
    }

    private async Task<T> SendForSingleAsync<T>(string relativeUrl, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, SerializerOptions);
        using var requestContent = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(relativeUrl, requestContent, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, content);
        return Deserialize<T>(content)
            ?? throw new BillingException($"Maxio returned an unexpected empty response for {relativeUrl}.", (int)response.StatusCode);
    }

    private static T Deserialize<T>(string content)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(content, SerializerOptions)
                ?? throw new BillingException("Maxio returned a response that could not be parsed.");
        }
        catch (JsonException ex)
        {
            throw new BillingException("Maxio returned a response that could not be parsed.", null, ex);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string content)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new BillingException(
            $"Maxio request failed with status {(int)response.StatusCode} ({response.StatusCode}): {Truncate(content)}",
            (int)response.StatusCode);
    }

    private static string Truncate(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= 1024 ? value : value[..1024] + "...");

    private sealed class ProductResponse
    {
        [JsonPropertyName("product")]
        public MaxioProductDto? Product { get; set; }
    }

    private sealed class MaxioProductDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("handle")]
        public string? Handle { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("price_in_cents")]
        public int PriceInCents { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonPropertyName("interval_unit")]
        public string? IntervalUnit { get; set; }

        [JsonPropertyName("archived_at")]
        public DateTimeOffset? ArchivedAt { get; set; }

        [JsonPropertyName("require_credit_card")]
        public bool? RequireCreditCard { get; set; }
    }

    private sealed class CustomerResponse
    {
        [JsonPropertyName("customer")]
        public MaxioCustomerDto? Customer { get; set; }
    }

    private sealed class MaxioCustomerDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        public MaxioCustomer ToCustomer() =>
            new(Id, Reference, FirstName ?? string.Empty, LastName, Email ?? string.Empty);
    }

    private sealed class SubscriptionResponse
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscriptionDto? Subscription { get; set; }
    }

    private sealed class MaxioSubscriptionDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("product_price_in_cents")]
        public int PriceInCents { get; set; }

        [JsonPropertyName("current_period_starts_at")]
        public DateTimeOffset? CurrentPeriodStartsAt { get; set; }

        [JsonPropertyName("current_period_ends_at")]
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

        [JsonPropertyName("next_assessment_at")]
        public DateTimeOffset? NextBillingAt { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }

        [JsonPropertyName("canceled_at")]
        public DateTimeOffset? CanceledAt { get; set; }

        [JsonPropertyName("product")]
        public MaxioNestedProductDto? Product { get; set; }

        [JsonPropertyName("customer")]
        public MaxioNestedCustomerDto? Customer { get; set; }

        public MaxioSubscriptionInfo ToSubscriptionInfo() =>
            new(Id,
                State ?? string.Empty,
                Product?.Handle ?? string.Empty,
                Product?.Name,
                PriceInCents,
                CurrentPeriodStartsAt,
                CurrentPeriodEndsAt,
                NextBillingAt,
                CreatedAt,
                CanceledAt);
    }

    private sealed class MaxioNestedProductDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("handle")]
        public string? Handle { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private sealed class MaxioNestedCustomerDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
    }
}
