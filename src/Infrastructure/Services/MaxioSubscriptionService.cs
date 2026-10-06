using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Recurring-subscription billing backed by the Maxio Advanced Billing REST API.
/// Maxio is the system of record: customers are keyed by the eShopOnWeb user's email
/// (the Maxio customer "reference"), which makes lookups idempotent across restarts.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const int MaxPageSize = 200;

    private static readonly string[] EndOfLifeStates =
    {
        "canceled",
        "expired",
        "failed_to_create"
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly KeyedAsyncLock _subscribeLock = new();

    public MaxioSubscriptionService(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioSubscriptionService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Maxio is not configured: 'Maxio:ApiKey' is missing. Set the MAXIO_API_KEY environment variable or configure user-secrets.");
        }

        if (string.IsNullOrWhiteSpace(_options.Subdomain) && string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException("Maxio is not configured: 'Maxio:Subdomain' (or 'Maxio:BaseUrl') is missing. Set the MAXIO_SITE_SUBDOMAIN environment variable or configure user-secrets.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio is not configured: 'Maxio:ProductFamilyHandle' is missing. Set the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable or configure user-secrets.");
        }
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await ListAllAsync<ProductResponse>("/products.json", cancellationToken);
        var components = await ListAllAsync<ComponentResponse>("/components.json", cancellationToken);

        var familyHandle = _options.ProductFamilyHandle!;
        var familyComponents = components
            .Where(c => c.Component?.ProductFamilyHandle == familyHandle)
            .Select(c => new SubscriptionComponent(
                c.Component!.Id,
                c.Component.Handle ?? string.Empty,
                c.Component.Name ?? string.Empty,
                c.Component.Kind ?? string.Empty,
                c.Component.UnitName,
                ParseDecimal(c.Component.UnitPrice),
                c.Component.PricePerUnitInCents))
            .ToList();

        return products
            .Where(p => p.Product?.ProductFamily?.Handle == familyHandle)
            .Select(p => new SubscriptionPlan(
                p.Product!.Id,
                p.Product.Handle ?? string.Empty,
                p.Product.Name ?? string.Empty,
                p.Product.Description,
                p.Product.PriceInCents,
                p.Product.Interval,
                p.Product.IntervalUnit ?? string.Empty,
                familyHandle,
                familyComponents))
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        using var _ = await _subscribeLock.WaitAsync(customerReference, cancellationToken);

        var customer = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            customer = await CreateCustomerAsync(customerReference, customerEmail, cancellationToken);
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = subscriptions.FirstOrDefault(s =>
            s.Product?.Handle == planHandle &&
            !EndOfLifeStates.Contains(s.State, StringComparer.OrdinalIgnoreCase));

        if (existing is not null)
        {
            _logger.LogInformation("Shopper {Reference} already has a live subscription {SubscriptionId} to plan {PlanHandle}; returning it.", customerReference, existing.Id, planHandle);
            return new SubscribeResult(Map(existing), created: false);
        }

        var created = await CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);
        _logger.LogInformation("Created Maxio subscription {SubscriptionId} for shopper {Reference} on plan {PlanHandle}.", created.Id, customerReference, planHandle);
        return new SubscribeResult(Map(created), created: true);
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionInfo>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await GetAsync<CustomerResponse>($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
            return response.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, CancellationToken cancellationToken)
    {
        var (firstName, lastName) = SplitName(email);
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerBody
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        try
        {
            var response = await PostAsync<CreateCustomerRequest, CustomerResponse>("/customers.json", request, cancellationToken);
            return response.Customer!;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            // Another request may have created the customer between our lookup and this create.
            // Re-lookup by reference; the reference is unique in Maxio.
            var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionBody
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                // The seeded plans do not require a payment method, so enroll on remittance
                // (invoice) collection to avoid needing card capture / 3-DS at signup.
                PaymentCollectionMethod = "remittance"
            }
        };

        var response = await PostAsync<CreateSubscriptionRequest, SubscriptionResponse>("/subscriptions.json", request, cancellationToken);
        return response.Subscription!;
    }

    private async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await GetAsync<List<SubscriptionResponse>>($"/customers/{customerId}/subscriptions.json", cancellationToken);
        return response
            .Where(s => s.Subscription is not null)
            .Select(s => s.Subscription!)
            .ToList();
    }

    private async Task<List<T>> ListAllAsync<T>(string path, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        var page = 1;
        while (true)
        {
            var separator = path.Contains('?') ? '&' : '?';
            var pagePath = $"{path}{separator}page={page}&per_page={MaxPageSize}";
            var batch = await GetAsync<List<T>>(pagePath, cancellationToken);
            results.AddRange(batch);
            if (batch.Count < MaxPageSize)
            {
                break;
            }

            page++;
        }

        return results;
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
        return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(
                $"Maxio API returned {(int)response.StatusCode} ({response.ReasonPhrase}) for {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}. Response: {Truncate(content)}",
                (int)response.StatusCode,
                content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions)!;
    }

    private static SubscriptionInfo Map(MaxioSubscription subscription)
    {
        return new SubscriptionInfo(
            subscription.Id,
            subscription.State ?? string.Empty,
            subscription.Customer?.Id ?? 0,
            subscription.Customer?.Reference,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            subscription.ProductPriceInCents,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt,
            subscription.ActivatedAt,
            subscription.CreatedAt,
            subscription.PaymentCollectionMethod);
    }

    private static (string FirstName, string LastName) SplitName(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (parts[0], string.Join(" ", parts.Skip(1)));
        }

        return (localPart, "Shopper");
    }

    private static decimal? ParseDecimal(string? value)
    {
        return decimal.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string Truncate(string value, int maxLength = 2000)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class CustomerResponse
    {
        [JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; set; }
    }

    private sealed class MaxioCustomer
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }
    }

    private sealed class CreateCustomerRequest
    {
        [JsonPropertyName("customer")]
        public CreateCustomerBody Customer { get; set; } = new();
    }

    private sealed class CreateCustomerBody
    {
        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }
    }

    private sealed class ProductResponse
    {
        [JsonPropertyName("product")]
        public MaxioProduct? Product { get; set; }
    }

    private sealed class MaxioProduct
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

        [JsonPropertyName("product_family")]
        public MaxioProductFamily? ProductFamily { get; set; }
    }

    private sealed class MaxioProductFamily
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("handle")]
        public string? Handle { get; set; }
    }

    private sealed class ComponentResponse
    {
        [JsonPropertyName("component")]
        public MaxioComponent? Component { get; set; }
    }

    private sealed class MaxioComponent
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("handle")]
        public string? Handle { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("unit_name")]
        public string? UnitName { get; set; }

        [JsonPropertyName("unit_price")]
        public string? UnitPrice { get; set; }

        [JsonPropertyName("price_per_unit_in_cents")]
        public int? PricePerUnitInCents { get; set; }

        [JsonPropertyName("product_family_handle")]
        public string? ProductFamilyHandle { get; set; }
    }

    private sealed class SubscriptionResponse
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription? Subscription { get; set; }
    }

    private sealed class MaxioSubscription
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("product_price_in_cents")]
        public int ProductPriceInCents { get; set; }

        [JsonPropertyName("current_period_ends_at")]
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

        [JsonPropertyName("next_assessment_at")]
        public DateTimeOffset? NextAssessmentAt { get; set; }

        [JsonPropertyName("activated_at")]
        public DateTimeOffset? ActivatedAt { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("payment_collection_method")]
        public string? PaymentCollectionMethod { get; set; }

        [JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; set; }

        [JsonPropertyName("product")]
        public MaxioProduct? Product { get; set; }
    }

    private sealed class CreateSubscriptionRequest
    {
        [JsonPropertyName("subscription")]
        public CreateSubscriptionBody Subscription { get; set; } = new();
    }

    private sealed class CreateSubscriptionBody
    {
        [JsonPropertyName("product_handle")]
        public string? ProductHandle { get; set; }

        [JsonPropertyName("customer_id")]
        public int? CustomerId { get; set; }

        [JsonPropertyName("payment_collection_method")]
        public string? PaymentCollectionMethod { get; set; }
    }
}
