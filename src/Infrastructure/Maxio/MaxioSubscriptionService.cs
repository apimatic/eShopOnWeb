using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="IMaxioSubscriptionService"/> implemented over the Maxio Advanced Billing REST
/// API using a typed <see cref="HttpClient"/>. Base address and HTTP Basic authentication are
/// configured by <see cref="MaxioServiceCollectionExtensions.AddMaxioSubscriptions"/>.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    /// <summary>
    /// Prefix for the Maxio customer <c>reference</c>, which is derived deterministically from
    /// the eShopOnWeb (Identity) user id. This is what makes "ensure a customer exists"
    /// idempotent — the same user always maps to the same Maxio customer.
    /// </summary>
    private const string CustomerReferencePrefix = "eshopweb-user-";

    /// <summary>
    /// Subscription states in which a shopper is considered already enrolled, so a repeat
    /// subscribe is a no-op. Terminal states (canceled, expired, trial_ended, …) are excluded
    /// so a shopper can re-subscribe after cancellation.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "past_due",
        "soft_failure", "on_hold", "paused", "suspended", "awaiting_signup",
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // Process-wide, per-customer serialization of subscribe requests so a double-click cannot
    // create two subscriptions. (Maxio's subscription-create has no idempotency key, so cross
    // -process protection additionally relies on the "already has a live subscription" check.)
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var products = await GetProductsAsync(familyId, cancellationToken);

        return products
            .Where(p => p.Handle is not null && p.ArchivedAt is null)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        // Confirm the requested plan exists in the configured family before mutating anything.
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var reference = BuildCustomerReference(subscriber.UserId);
        var gate = SubscribeLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, reference, cancellationToken);

            // Idempotency: if the shopper already has a live subscription to this plan, return it.
            var existing = (await GetSubscriptionsForCustomerAsync(customer.Id, cancellationToken))
                .FirstOrDefault(s =>
                    string.Equals(s.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase)
                    && LiveStates.Contains(s.State));
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Maxio: customer {CustomerId} already has live subscription {SubscriptionId} to plan {PlanHandle}; returning existing.",
                    customer.Id, existing.Id, plan.Handle);
                return new SubscribeResult(existing, alreadyExisted: true);
            }

            var request = new CreateSubscriptionRequest
            {
                Subscription = new SubscriptionAttributes
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    // Bill by remittance (invoice) so enrollment succeeds without a stored
                    // payment method / card capture, per the configured no-card plans.
                    PaymentCollectionMethod = "remittance",
                },
            };

            using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, JsonOptions, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var envelope = await ReadJsonAsync<SubscriptionEnvelope>(response, cancellationToken);
            var created = MapSubscription(envelope.Subscription);
            _logger.LogInformation(
                "Maxio: created subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
                created.Id, customer.Id, plan.Handle);
            return new SubscribeResult(created, alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var reference = BuildCustomerReference(subscriber.UserId);
        var customer = await LookupCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await GetSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
    }

    // ---- Maxio operations ----

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("product_families.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var families = await ReadJsonAsync<List<ProductFamilyEnvelope>>(response, cancellationToken);
        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null
                && string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new MaxioApiException(
                $"Configured Maxio product family '{_settings.ProductFamilyHandle}' was not found on this site.");
        }

        return match.Id;
    }

    private async Task<List<ProductResource>> GetProductsAsync(int familyId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"product_families/{familyId}/products.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await ReadJsonAsync<List<ProductEnvelope>>(response, cancellationToken);
        return envelopes.Select(e => e.Product).Where(p => p is not null).Select(p => p!).ToList();
    }

    private async Task<CustomerResource> EnsureCustomerAsync(
        SubscriberIdentity subscriber, string reference, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CustomerAttributes
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = reference,
            },
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", request, JsonOptions, cancellationToken);

        // A concurrent request may have created the customer first; Maxio enforces reference
        // uniqueness and returns 422. Recover by re-reading the now-existing customer.
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await LookupCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<CustomerEnvelope>(response, cancellationToken);
        if (envelope.Customer is null)
        {
            throw new MaxioApiException("Maxio returned an empty customer on create.");
        }

        _logger.LogInformation(
            "Maxio: created customer {CustomerId} for reference {Reference}.", envelope.Customer.Id, reference);
        return envelope.Customer;
    }

    private async Task<CustomerResource?> LookupCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken)
    {
        var uri = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(uri, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope.Customer;
    }

    private async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForCustomerAsync(
        int customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await ReadJsonAsync<List<SubscriptionEnvelope>>(response, cancellationToken);
        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s))
            .ToList();
    }

    // ---- Mapping ----

    private SubscriptionPlan MapPlan(ProductResource p) => new(
        productId: p.Id,
        handle: p.Handle!,
        name: p.Name ?? p.Handle!,
        description: p.Description,
        priceInCents: p.PriceInCents,
        interval: p.Interval,
        intervalUnit: p.IntervalUnit ?? "month",
        productFamilyHandle: p.ProductFamily?.Handle ?? _settings.ProductFamilyHandle,
        requiresPaymentMethod: p.RequireCreditCard);

    private static CustomerSubscription MapSubscription(SubscriptionResource? s)
    {
        if (s is null)
        {
            throw new MaxioApiException("Maxio returned an empty subscription.");
        }

        return new CustomerSubscription(
            id: s.Id,
            state: s.State ?? "unknown",
            planHandle: s.Product?.Handle ?? string.Empty,
            planName: s.Product?.Name ?? s.Product?.Handle ?? string.Empty,
            priceInCents: s.ProductPriceInCents != 0 ? s.ProductPriceInCents : (s.Product?.PriceInCents ?? 0),
            currency: string.IsNullOrWhiteSpace(s.Currency) ? "USD" : s.Currency!,
            currentPeriodStartedAt: s.CurrentPeriodStartedAt,
            currentPeriodEndsAt: s.CurrentPeriodEndsAt,
            nextBillingAt: s.NextAssessmentAt,
            customerId: s.Customer?.Id ?? 0,
            customerReference: s.Customer?.Reference);
    }

    // ---- Helpers ----

    private static string BuildCustomerReference(string userId) => CustomerReferencePrefix + userId;

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        if (value is null)
        {
            throw new MaxioApiException("Maxio returned an empty or unreadable response body.");
        }

        return value;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ExtractErrors(body);
        _logger.LogWarning(
            "Maxio: {Method} {Uri} returned {StatusCode}: {Errors}",
            response.RequestMessage?.Method, response.RequestMessage?.RequestUri, (int)response.StatusCode,
            errors.Count > 0 ? string.Join("; ", errors) : body);
        throw new MaxioApiException((int)response.StatusCode, errors);
    }

    /// <summary>
    /// Flattens Maxio error payloads, which appear either as {"errors": ["msg", …]} or as
    /// {"errors": {"field": ["msg", …]}}.
    /// </summary>
    private static IReadOnlyList<string> ExtractErrors(string body)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return messages;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                CollectStrings(errorsElement, messages);
            }
        }
        catch (JsonException)
        {
            // Non-JSON body (e.g. an HTML error page); leave messages empty.
        }

        return messages;
    }

    private static void CollectStrings(JsonElement element, List<string> sink)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    sink.Add(value!);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectStrings(item, sink);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectStrings(property.Value, sink);
                }

                break;
        }
    }
}
