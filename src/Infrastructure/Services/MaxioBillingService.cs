using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Maxio Advanced Billing client. Implements the Billing API as documented on the
/// Billing API site: HTTP Basic auth (API key as username, "X" as password) against
/// https://{subdomain}.chargify.com (or the configured Maxio:BaseUrl override).
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    // States in which a subscription is still live; a shopper holding one of these for a
    // plan is already subscribed and must not be enrolled again (idempotency guard).
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended", "on_hold", "suspended"
    };

    // Serializes subscribe calls per shopper so a double-click cannot race past the
    // existing-subscription check.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        }

        // The product family path parameter accepts "handle:{handle}" per the Billing API docs.
        var products = await GetAsync<List<ProductEnvelope>>(
            $"product_families/handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle)}/products.json",
            cancellationToken);

        return (products ?? new List<ProductEnvelope>())
            .Where(p => p.Product is not null && p.Product.ArchivedAt is null)
            .Select(p => new SubscriptionPlan
            {
                ProductId = p.Product!.Id,
                Handle = p.Product.Handle ?? string.Empty,
                Name = p.Product.Name ?? string.Empty,
                Description = p.Product.Description,
                PriceInCents = p.Product.PriceInCents,
                Interval = p.Product.Interval,
                IntervalUnit = p.Product.IntervalUnit ?? string.Empty
            })
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(ShopperInfo shopper, string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required to subscribe.", nameof(productHandle));
        }

        var gate = SubscribeLocks.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customerId = await EnsureCustomerAsync(shopper, cancellationToken);

            var existing = await ListSubscriptionsAsync(customerId, cancellationToken);
            var current = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
                !EndOfLifeStates.Contains(s.State ?? string.Empty));

            if (current is not null)
            {
                _logger.LogInformation(
                    "Shopper {UserId} already has a {State} subscription {SubscriptionId} for {ProductHandle}; returning it instead of creating a duplicate.",
                    shopper.UserId, current.State, current.Id, productHandle);
                return new SubscribeResult(Map(current), AlreadyExisted: true);
            }

            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    // eShopOnWeb never captures cards; remittance means Maxio invoices the
                    // shopper instead of auto-charging a payment method on file.
                    PaymentCollectionMethod = "remittance"
                }
            };

            var created = await PostAsync<CreateSubscriptionRequest, SubscriptionEnvelope>(
                "subscriptions.json", request, cancellationToken);

            if (created?.Subscription is null)
            {
                throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty subscription response.");
            }

            _logger.LogInformation(
                "Created subscription {SubscriptionId} for shopper {UserId} on plan {ProductHandle}.",
                created.Subscription.Id, shopper.UserId, productHandle);

            return new SubscribeResult(Map(created.Subscription), AlreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(ShopperInfo shopper, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<int> EnsureCustomerAsync(ShopperInfo shopper, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = shopper.FirstName,
                LastName = shopper.LastName,
                Email = shopper.Email,
                Reference = shopper.UserId
            }
        };

        try
        {
            var created = await PostAsync<CreateCustomerRequest, CustomerEnvelope>("customers.json", request, cancellationToken);
            if (created?.Customer is null)
            {
                throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty customer response.");
            }

            return created.Customer.Id;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // The reference is unique per customer; a concurrent request may have created
            // the customer first. Re-read instead of failing.
            var winner = await FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (winner is not null)
            {
                return winner.Id;
            }

            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Customer;
    }

    private async Task<List<MaxioSubscription>> ListSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<SubscriptionEnvelope>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        return (envelopes ?? new List<SubscriptionEnvelope>())
            .Where(e => e.Subscription is not null)
            .Select(e => e.Subscription!)
            .ToList();
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, body, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var details = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var error = JsonSerializer.Deserialize<ErrorEnvelope>(details);
            if (error?.Errors is { Count: > 0 })
            {
                details = string.Join("; ", error.Errors);
            }
        }
        catch (JsonException)
        {
            // keep the raw body
        }

        throw new MaxioApiException(response.StatusCode, $"Maxio API request failed ({(int)response.StatusCode}): {details}");
    }

    private static CustomerSubscription Map(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State ?? string.Empty,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        ActivatedAt = subscription.ActivatedAt,
        NextBillingAt = subscription.CurrentPeriodEndsAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod
    };

    private sealed class ErrorEnvelope
    {
        [JsonPropertyName("errors")] public List<string>? Errors { get; set; }
    }

    private sealed class CustomerEnvelope
    {
        [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
    }

    private sealed class MaxioCustomer
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("reference")] public string? Reference { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
    }

    private sealed class CreateCustomerRequest
    {
        [JsonPropertyName("customer")] public CreateCustomer Customer { get; set; } = new();
    }

    private sealed class CreateCustomer
    {
        [JsonPropertyName("first_name")] public string FirstName { get; set; } = string.Empty;
        [JsonPropertyName("last_name")] public string LastName { get; set; } = string.Empty;
        [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
        [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
    }

    private sealed class ProductEnvelope
    {
        [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
    }

    private sealed class MaxioProduct
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("handle")] public string? Handle { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("price_in_cents")] public long PriceInCents { get; set; }
        [JsonPropertyName("interval")] public int Interval { get; set; }
        [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; set; }
        [JsonPropertyName("archived_at")] public DateTime? ArchivedAt { get; set; }
    }

    private sealed class SubscriptionEnvelope
    {
        [JsonPropertyName("subscription")] public MaxioSubscription? Subscription { get; set; }
    }

    private sealed class MaxioSubscription
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("product_price_in_cents")] public long ProductPriceInCents { get; set; }
        [JsonPropertyName("current_period_ends_at")] public DateTime? CurrentPeriodEndsAt { get; set; }
        [JsonPropertyName("activated_at")] public DateTime? ActivatedAt { get; set; }
        [JsonPropertyName("cancel_at_end_of_period")] public bool? CancelAtEndOfPeriod { get; set; }
        [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
    }

    private sealed class CreateSubscriptionRequest
    {
        [JsonPropertyName("subscription")] public CreateSubscription Subscription { get; set; } = new();
    }

    private sealed class CreateSubscription
    {
        [JsonPropertyName("product_handle")] public string ProductHandle { get; set; } = string.Empty;
        [JsonPropertyName("customer_id")] public int CustomerId { get; set; }
        [JsonPropertyName("payment_collection_method")] public string PaymentCollectionMethod { get; set; } = "remittance";
    }
}
