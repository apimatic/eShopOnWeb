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
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Maxio Advanced Billing REST client (US data center API shape, verified against
/// developers.maxio.com and the live sandbox):
///   base address  https://{site}.chargify.com (or Maxio:BaseUrl override)
///   auth          HTTP Basic with the API key as username
///   paths         /customers.json, /customers/lookup.json, /product_families.json,
///                 /products.json, /subscriptions.json
/// </summary>
public class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    // Plans in the eShopOnWeb catalog are seeded with "payment method not required"; collecting via
    // invoice lets signup complete without card capture or 3-DS.
    private const string SIGNUP_PAYMENT_COLLECTION_METHOD = "invoice";

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public MaxioAdvancedBillingClient(HttpClient httpClient, MaxioSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public async Task<IReadOnlyList<MaxioPlan>> GetPlansForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(productFamilyHandle, nameof(productFamilyHandle));

        var families = await GetAsync<List<ProductFamilyEnvelope>>("/product_families.json", cancellationToken);
        var family = families
            .Select(e => e.ProductFamily)
            .FirstOrDefault(f => string.Equals(f.Handle, productFamilyHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new MaxioBillingProviderException($"Maxio product family '{productFamilyHandle}' was not found on the configured site.", (int)HttpStatusCode.NotFound);

        var products = await GetAsync<List<ProductEnvelope>>($"/products.json?filter%5Bproduct_family_id%5D={family.Id}", cancellationToken);

        return products
            .Select(e => e.Product)
            .Select(p => new MaxioPlan(p.Id, p.Handle ?? p.Name, p.Name, p.Description,
                p.PriceInCents ?? 0, p.Interval ?? 1, p.IntervalUnit ?? "month", p.ArchivedAt != null))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(reference, nameof(reference));

        var request = new HttpRequestMessage(HttpMethod.Get, $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await ReadSuccessAsync<CustomerEnvelope>(response, cancellationToken);
        return new MaxioCustomer(envelope.Customer.Id, envelope.Customer.Reference, envelope.Customer.Email);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string? email, CancellationToken cancellationToken = default)
    {
        var body = new CustomerRequestEnvelope
        {
            Customer = new CustomerRequest
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference,
            }
        };

        try
        {
            var envelope = await PostAsync<CustomerRequestEnvelope, CustomerEnvelope>("/customers.json", body, cancellationToken);
            return new MaxioCustomer(envelope.Customer.Id, envelope.Customer.Reference, envelope.Customer.Email);
        }
        catch (MaxioBillingProviderException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity &&
            ex.ProviderErrors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase) && e.Contains("unique", StringComparison.OrdinalIgnoreCase)))
        {
            throw new MaxioBillingProviderException($"A Maxio customer with reference '{reference}' already exists.", ex.StatusCode, ex.ProviderErrors, duplicateCustomerReference: true);
        }
    }

    public async Task<IReadOnlyList<MaxioSubscriptionInfo>> GetSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var envelopes = await GetAsync<List<SubscriptionEnvelope>>($"/subscriptions.json?customer_id={customerId}", cancellationToken);
        return envelopes.Select(e => ToInfo(e.Subscription)).ToList();
    }

    public async Task<MaxioSubscriptionInfo> CreateSubscriptionAsync(int customerId, MaxioPlan plan, CancellationToken cancellationToken = default)
    {
        var body = new SubscriptionRequestEnvelope
        {
            Subscription = new SubscriptionRequest
            {
                CustomerId = customerId,
                ProductId = plan.ProductId,
                PaymentCollectionMethod = SIGNUP_PAYMENT_COLLECTION_METHOD,
            }
        };

        var envelope = await PostAsync<SubscriptionRequestEnvelope, SubscriptionEnvelope>("/subscriptions.json", body, cancellationToken);
        return ToInfo(envelope.Subscription);
    }

    private async Task<TResponse> GetAsync<TResponse>(string path, CancellationToken cancellationToken)
    {
        var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);
        return await ReadSuccessAsync<TResponse>(response, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json"),
        };
        var response = await SendAsync(request, cancellationToken);
        return await ReadSuccessAsync<TResponse>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new MaxioConfigurationException("No Maxio API key is configured. Set 'Maxio:ApiKey' (e.g. via user-secrets or the MAXIO_API_KEY environment variable).");
        }

        if (_httpClient.BaseAddress == null)
        {
            throw new MaxioConfigurationException("The Maxio API base address is not configured. Set 'Maxio:BaseUrl' or 'Maxio:Subdomain'.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ApiKey}:x")));

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioBillingProviderException($"Could not reach the Maxio Advanced Billing API: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioBillingProviderException($"The Maxio Advanced Billing API request timed out: {ex.Message}");
        }
    }

    private static async Task<T> ReadSuccessAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioBillingProviderException(
                $"Maxio Advanced Billing API returned {(int)response.StatusCode}: {ExtractErrors(body) ?? body}",
                (int)response.StatusCode,
                providerErrors: ExtractErrorList(body));
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize<T>(body, JsonOptions);
            if (deserialized == null)
            {
                throw new JsonException("Empty payload");
            }
            return deserialized;
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingProviderException($"Unexpected response from the Maxio Advanced Billing API: {ex.Message}", (int)response.StatusCode);
        }
    }

    private static string? ExtractErrors(string body)
    {
        var errors = ExtractErrorList(body);
        return errors.Count > 0 ? string.Join(" ", errors) : null;
    }

    private static IReadOnlyList<string> ExtractErrorList(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                if (errorsElement.ValueKind == JsonValueKind.Array)
                {
                    return errorsElement.EnumerateArray().Select(e => e.ToString()).ToList();
                }
                if (errorsElement.ValueKind == JsonValueKind.Object)
                {
                    return errorsElement.EnumerateObject()
                        .SelectMany(p => p.Value.ValueKind == JsonValueKind.Array
                            ? p.Value.EnumerateArray().Select(v => $"{p.Name}: {v}")
                            : new[] { $"{p.Name}: {p.Value}" })
                        .ToList();
                }
            }
        }
        catch (JsonException)
        {
            // fall through - caller uses the raw body
        }
        return Array.Empty<string>();
    }

    private static MaxioSubscriptionInfo ToInfo(Subscription subscription) =>
        new MaxioSubscriptionInfo(
            subscription.Id,
            subscription.Customer?.Id ?? 0,
            subscription.Product?.Id ?? 0,
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.ProductPriceInCents ?? 0,
            subscription.State,
            subscription.NextAssessmentAt,
            subscription.CurrentPeriodStartedAt,
            subscription.CurrentPeriodEndsAt,
            subscription.CreatedAt);

    #region Wire shapes (JSON envelopes as returned/accepted by the Advanced Billing API)

    private class ProductFamilyEnvelope
    {
        public ProductFamily ProductFamily { get; set; } = new();
    }

    private class ProductFamily
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Handle { get; set; }
        public DateTimeOffset? ArchivedAt { get; set; }
    }

    private class ProductEnvelope
    {
        public Product Product { get; set; } = new();
    }

    private class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Handle { get; set; }
        public string? Description { get; set; }
        public int? PriceInCents { get; set; }
        public int? Interval { get; set; }
        public string? IntervalUnit { get; set; }
        public DateTimeOffset? ArchivedAt { get; set; }
    }

    private class CustomerRequestEnvelope
    {
        public CustomerRequest Customer { get; set; } = new();
    }

    private class CustomerRequest
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string Reference { get; set; } = string.Empty;
    }

    private class CustomerEnvelope
    {
        public Customer Customer { get; set; } = new();
    }

    private class Customer
    {
        public int Id { get; set; }
        public string? Reference { get; set; }
        public string? Email { get; set; }
    }

    private class SubscriptionRequestEnvelope
    {
        public SubscriptionRequest Subscription { get; set; } = new();
    }

    private class SubscriptionRequest
    {
        public int CustomerId { get; set; }
        public int ProductId { get; set; }
        public string PaymentCollectionMethod { get; set; } = string.Empty;
    }

    private class SubscriptionEnvelope
    {
        public Subscription Subscription { get; set; } = new();
    }

    private class Subscription
    {
        public int Id { get; set; }
        public string State { get; set; } = string.Empty;
        public int? ProductPriceInCents { get; set; }
        public SubscriptionProduct? Product { get; set; }
        public SubscriptionCustomer? Customer { get; set; }
        public DateTimeOffset? NextAssessmentAt { get; set; }
        public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
    }

    private class SubscriptionProduct
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Handle { get; set; }
    }

    private class SubscriptionCustomer
    {
        public int Id { get; set; }
    }

    #endregion
}
