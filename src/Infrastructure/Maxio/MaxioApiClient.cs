using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Wire = Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Talks to Maxio Advanced Billing over its JSON REST API and maps the results onto the
/// ApplicationCore subscription models. Authentication, retries and timeouts are configured on the
/// injected <see cref="HttpClient"/>; this type only owns request shapes and error translation.
/// </summary>
public class MaxioApiClient : IBillingGateway
{
    private const string PlanCacheKey = "maxio:plans";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient,
        MaxioSettings settings,
        IMemoryCache cache,
        ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(PlanCacheKey, out IReadOnlyCollection<SubscriptionPlan>? cached) && cached is not null)
            return cached;

        // Handles are the stable identifier for a product family; ids are reassigned on re-seed.
        var path = $"product_families/handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle)}/products.json?per_page=200";
        var envelopes = await GetAsync<List<Wire.ProductEnvelope>>(path, "list plans", cancellationToken)
                        ?? new List<Wire.ProductEnvelope>();

        var plans = envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p!.Handle))
            // An archived product is still returned by some queries but can no longer be sold.
            .Where(p => p!.ArchivedAt is null)
            .Select(p => MapPlan(p!))
            .OrderBy(p => p.PriceInCents)
            .ToList();

        _logger.LogInformation("Loaded {PlanCount} subscription plans from Maxio product family '{Family}'.",
            plans.Count, _settings.ProductFamilyHandle);

        if (_settings.PlanCacheSeconds > 0)
        {
            _cache.Set(PlanCacheKey, (IReadOnlyCollection<SubscriptionPlan>)plans,
                TimeSpan.FromSeconds(_settings.PlanCacheSeconds));
        }

        return plans;
    }

    public async Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle)) return null;

        // Resolved against the offered plans rather than by direct product lookup, so a product
        // outside the configured family can never be subscribed to.
        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        // Maxio answers 404 when no customer carries the reference, which is a normal outcome here.
        var envelope = await GetAsync<Wire.CustomerEnvelope>(path, "look up customer", cancellationToken,
            treatNotFoundAsNull: true);

        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(NewCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new Wire.CreateCustomerRequest
        {
            Customer = new Wire.CustomerAttributes
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Reference = request.Reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", payload, SerializerOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errors = await ReadErrorsAsync(response, cancellationToken);

            // Maxio enforces reference uniqueness with a validation error rather than a conflict
            // status, so the message is what tells us the customer already exists.
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity && MentionsReferenceTaken(errors))
                throw new DuplicateBillingReferenceException(request.Reference);

            throw BillingGatewayException.FromResponse("create customer", (int)response.StatusCode, errors);
        }

        var envelope = await DeserializeAsync<Wire.CustomerEnvelope>(response, "create customer", cancellationToken);
        if (envelope?.Customer is null)
            throw new BillingGatewayException("Maxio accepted the customer but returned no customer in the response.");

        return MapCustomer(envelope.Customer);
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";
        var envelopes = await GetAsync<List<Wire.SubscriptionEnvelope>>(path, "list customer subscriptions",
            cancellationToken, treatNotFoundAsNull: true);

        return envelopes?
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList() ?? (IReadOnlyCollection<CustomerSubscription>)Array.Empty<CustomerSubscription>();
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(NewSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new Wire.CreateSubscriptionRequest
        {
            Subscription = new Wire.SubscriptionAttributes
            {
                ProductHandle = request.PlanHandle,
                ProductPricePointHandle = request.PricePointHandle,
                CustomerId = request.CustomerId,
                Reference = request.Reference,
                PaymentCollectionMethod = request.PaymentCollectionMethod
            },
            UniquenessToken = request.UniquenessToken
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, SerializerOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errors = await ReadErrorsAsync(response, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Conflict)
                throw new DuplicateBillingSubmissionException(request.UniquenessToken);

            throw BillingGatewayException.FromResponse("create subscription", (int)response.StatusCode, errors);
        }

        var envelope = await DeserializeAsync<Wire.SubscriptionEnvelope>(response, "create subscription", cancellationToken);
        if (envelope?.Subscription is null)
            throw new BillingGatewayException("Maxio accepted the subscription but returned no subscription in the response.");

        return MapSubscription(envelope.Subscription);
    }

    private async Task<T?> GetAsync<T>(string path,
        string operation,
        CancellationToken cancellationToken,
        bool treatNotFoundAsNull = false)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound) return default;

        if (!response.IsSuccessStatusCode)
        {
            var errors = await ReadErrorsAsync(response, cancellationToken);
            throw BillingGatewayException.FromResponse(operation, (int)response.StatusCode, errors);
        }

        return await DeserializeAsync<T>(response, operation, cancellationToken);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new BillingGatewayException($"Maxio returned a response for '{operation}' that could not be read.", ex);
        }
    }

    /// <summary>
    /// Flattens Maxio's error payload, which is an array of strings on some endpoints and an
    /// object keyed by field on others.
    /// </summary>
    private static async Task<IReadOnlyCollection<string>> ReadErrorsAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<Wire.ErrorEnvelope>(SerializerOptions, cancellationToken);
            if (envelope is null) return Array.Empty<string>();

            var messages = new List<string>();

            switch (envelope.Errors.ValueKind)
            {
                case JsonValueKind.Array:
                    messages.AddRange(envelope.Errors.EnumerateArray()
                        .Select(e => e.ToString())
                        .Where(m => !string.IsNullOrWhiteSpace(m)));
                    break;
                case JsonValueKind.Object:
                    messages.AddRange(envelope.Errors.EnumerateObject()
                        .Select(p => $"{p.Name}: {p.Value}"));
                    break;
                case JsonValueKind.String:
                    messages.Add(envelope.Errors.GetString()!);
                    break;
            }

            if (!string.IsNullOrWhiteSpace(envelope.Error)) messages.Add(envelope.Error!);

            return messages;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // A gateway or proxy failure can answer with HTML; there is nothing to extract.
            return Array.Empty<string>();
        }
    }

    private static bool MentionsReferenceTaken(IReadOnlyCollection<string> errors) =>
        errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
                        (e.Contains("taken", StringComparison.OrdinalIgnoreCase) ||
                         e.Contains("unique", StringComparison.OrdinalIgnoreCase)));

    private static SubscriptionPlan MapPlan(Wire.Product product) => new(
        product.Handle!,
        product.Name ?? product.Handle!,
        product.Description,
        ToInt32Cents(product.PriceInCents),
        product.Interval,
        product.IntervalUnit ?? "month",
        product.ProductPricePointHandle,
        product.RequireCreditCard,
        product.ProductFamily?.Handle);

    private static BillingCustomer MapCustomer(Wire.Customer customer) => new(
        customer.Id,
        customer.Reference,
        customer.Email ?? string.Empty,
        customer.FirstName ?? string.Empty,
        customer.LastName ?? string.Empty);

    private static CustomerSubscription MapSubscription(Wire.Subscription subscription) => new(
        subscription.Id,
        subscription.Reference,
        subscription.State ?? "unknown",
        subscription.Product?.Handle,
        subscription.Product?.Name,
        ToInt32Cents(subscription.ProductPriceInCents),
        subscription.Product?.Interval ?? 0,
        subscription.Product?.IntervalUnit,
        subscription.CurrentPeriodStartedAt,
        subscription.CurrentPeriodEndsAt,
        // next_assessment_at is when a charge will actually be attempted; it tracks the period end
        // except after a failed renewal, when the retry time is the honest answer.
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        subscription.ActivatedAt,
        subscription.CanceledAt,
        subscription.PaymentCollectionMethod,
        subscription.Customer?.Id ?? 0,
        subscription.Customer?.Reference);

    // Maxio types money as a 64-bit cent count; no realistic plan price overflows an int, but
    // clamp rather than throw if one ever does.
    private static int ToInt32Cents(long cents) =>
        cents > int.MaxValue ? int.MaxValue : cents < int.MinValue ? int.MinValue : (int)cents;
}
