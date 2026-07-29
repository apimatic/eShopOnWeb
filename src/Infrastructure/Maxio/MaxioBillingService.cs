using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="IMaxioBillingService"/> backed by the Maxio Advanced Billing REST API.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    // Maxio subscription states that mean the customer still holds the plan. Anything else is an
    // end-of-life state, so a fresh subscribe should be allowed rather than short-circuited.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "paused", "past_due", "soft_failure", "awaiting_signup"
    };

    // Payment collection method per site base address, resolved once from the site's architecture.
    private static readonly ConcurrentDictionary<string, string> CollectionMethodCache = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IAppLogger<MaxioBillingService> _logger;
    private readonly string _productFamilyHandle;

    public MaxioBillingService(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _productFamilyHandle = settings.Value.ProductFamilyHandle;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(_productFamilyHandle)}/products.json?per_page=200";
        using var response = await SendAsync(HttpMethod.Get, path, body: null, cancellationToken);

        await EnsureSuccessAsync(response, "list subscription plans", cancellationToken);

        var products = await ReadJsonAsync<List<MaxioProductEnvelope>>(response, cancellationToken) ?? new();

        return products
            .Select(p => p.Product)
            .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => new SubscriptionPlan
            {
                ProductId = p!.Id,
                Handle = p.Handle!,
                Name = p.Name ?? p.Handle!,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty,
                RequiresPaymentMethod = p.RequireCreditCard
            })
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        MaxioCustomerIdentity identity,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioBillingException("A plan handle is required to subscribe.", (int)HttpStatusCode.BadRequest);
        }

        // Validate the plan against the configured product family so we never send an unknown handle to Maxio.
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new MaxioBillingException(
                $"Plan '{planHandle}' is not an available subscription plan.",
                (int)HttpStatusCode.BadRequest);
        }

        var customer = await EnsureCustomerAsync(identity, cancellationToken);

        // Idempotency: if the customer already holds a live subscription to this plan, return it.
        var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Maxio customer {CustomerId} already has live subscription {SubscriptionId} to plan {Plan}; skipping create.",
                customer.Id, existing.Id, plan.Handle);
            return new SubscribeResult(existing, wasCreated: false, customer.Id);
        }

        var (subscription, created) = await CreateSubscriptionAsync(customer.Id, plan.Handle, identity.Reference, cancellationToken);
        return new SubscribeResult(subscription, wasCreated: created, customer.Id);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken = default)
    {
        var customer = await LookupCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    // ----- Customer helpers -----

    private async Task<MaxioCustomer> EnsureCustomerAsync(MaxioCustomerIdentity identity, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerAsync(identity.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new CreateCustomerRequest(new CustomerAttributes
        {
            FirstName = identity.FirstName,
            LastName = identity.LastName,
            Email = identity.Email,
            Reference = identity.Reference
        });

        using var response = await SendAsync(HttpMethod.Post, "customers.json", request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var created = (await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken))?.Customer
                ?? throw new MaxioBillingException("Maxio returned an empty customer on create.");
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", created.Id, identity.Reference);
            return created;
        }

        // A concurrent request (e.g. a double-click) may have created the customer first; the unique
        // reference constraint then yields a 422. Recover by reading the now-existing customer.
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var recovered = await LookupCustomerAsync(identity.Reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }
        }

        throw await BuildExceptionAsync(response, "create Maxio customer", cancellationToken);
    }

    private async Task<MaxioCustomer?> LookupCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(HttpMethod.Get, path, body: null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up Maxio customer", cancellationToken);
        return (await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken))?.Customer;
    }

    // ----- Subscription helpers -----

    private async Task<(CustomerSubscription Subscription, bool Created)> CreateSubscriptionAsync(
        int customerId,
        string planHandle,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var collectionMethod = await ResolvePaymentCollectionMethodAsync(cancellationToken);

        var request = new CreateSubscriptionRequest
        {
            Subscription = new SubscriptionAttributes
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = collectionMethod
            },
            UniquenessToken = BuildUniquenessToken(customerReference, planHandle)
        };

        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var created = (await ReadJsonAsync<MaxioSubscriptionEnvelope>(response, cancellationToken))?.Subscription
                ?? throw new MaxioBillingException("Maxio returned an empty subscription on create.");
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} ({State}) for customer {CustomerId} on plan {Plan}.",
                created.Id, created.State ?? "unknown", customerId, planHandle);
            return (MapSubscription(created), true);
        }

        // Duplicate-prevention: a concurrent identical create won the race. Return the live subscription it made.
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var live = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
            if (live is not null)
            {
                _logger.LogInformation(
                    "Create for customer {CustomerId} on plan {Plan} hit duplicate-prevention; returning existing subscription {SubscriptionId}.",
                    customerId, planHandle, live.Id);
                return (live, false);
            }

            // No live subscription exists, so the token collided with an older, non-live subscription.
            // Retry once with a fresh token to force creation.
            var retry = new CreateSubscriptionRequest
            {
                Subscription = new SubscriptionAttributes
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = collectionMethod
                },
                UniquenessToken = Guid.NewGuid().ToString("N")
            };
            using var retryResponse = await SendAsync(HttpMethod.Post, "subscriptions.json", retry, cancellationToken);
            await EnsureSuccessAsync(retryResponse, "create Maxio subscription", cancellationToken);
            var retryCreated = (await ReadJsonAsync<MaxioSubscriptionEnvelope>(retryResponse, cancellationToken))?.Subscription
                ?? throw new MaxioBillingException("Maxio returned an empty subscription on create.");
            return (MapSubscription(retryCreated), true);
        }

        throw await BuildExceptionAsync(response, "create Maxio subscription", cancellationToken);
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions
            .Where(s => string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase))
            .Where(s => LiveStates.Contains(s.State))
            .OrderByDescending(s => s.Id)
            .FirstOrDefault();
    }

    private async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", body: null, cancellationToken);
        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);

        var envelopes = await ReadJsonAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken) ?? new();
        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    private static CustomerSubscription MapSubscription(MaxioSubscription s) => new()
    {
        Id = s.Id,
        State = s.State ?? "unknown",
        PlanHandle = s.Product?.Handle,
        PlanName = s.Product?.Name,
        ProductPriceInCents = s.ProductPriceInCents,
        Interval = s.Product?.Interval ?? 0,
        IntervalUnit = s.Product?.IntervalUnit,
        NextBillingAt = s.CurrentPeriodEndsAt ?? s.NextAssessmentAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        CreatedAt = s.CreatedAt,
        CustomerId = s.Customer?.Id ?? 0,
        CustomerReference = s.Customer?.Reference
    };

    // Chooses the payment collection method valid for the site's architecture so a paid plan can be
    // started without a stored card: "remittance" on Relationship Invoicing sites, "invoice" on
    // statement-based sites. Either generates an invoice instead of attempting an auto-charge at signup.
    private async Task<string> ResolvePaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var cacheKey = _httpClient.BaseAddress?.ToString() ?? string.Empty;
        if (CollectionMethodCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        using var response = await SendAsync(HttpMethod.Get, "site.json", body: null, cancellationToken);
        await EnsureSuccessAsync(response, "read Maxio site", cancellationToken);

        var site = (await ReadJsonAsync<MaxioSiteEnvelope>(response, cancellationToken))?.Site;
        var method = site?.RelationshipInvoicingEnabled == true ? "remittance" : "invoice";

        CollectionMethodCache[cacheKey] = method;
        _logger.LogInformation("Maxio site payment collection method resolved to '{Method}'.", method);
        return method;
    }

    // Deterministic per (customer, plan) so a rapid double-submit collides (409) instead of double-charging.
    private static string BuildUniquenessToken(string customerReference, string planHandle)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"eshoponweb:{customerReference}:{planHandle}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ----- HTTP plumbing -----

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, mediaType: null, JsonOptions);
        }

        return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildExceptionAsync(response, operation, cancellationToken);
        }
    }

    private async Task<MaxioBillingException> BuildExceptionAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ParseErrors(body);
        var status = (int)response.StatusCode;

        // Map Maxio failures onto sensible API responses: 422/429/4xx → client-ish, everything else → 502.
        var surfacedStatus = response.StatusCode switch
        {
            HttpStatusCode.UnprocessableEntity => (int)HttpStatusCode.BadRequest,
            HttpStatusCode.TooManyRequests => (int)HttpStatusCode.TooManyRequests,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => (int)HttpStatusCode.BadGateway,
            _ => (int)HttpStatusCode.BadGateway
        };

        var detail = errors.Count > 0
            ? string.Join("; ", errors)
            : $"Maxio returned HTTP {status}.";

        _logger.LogWarning("Failed to {Operation}: HTTP {Status} — {Detail}", operation, status, detail);
        return new MaxioBillingException($"Unable to {operation}. {detail}", surfacedStatus, errors);
    }

    // Maxio error bodies come as {"errors":[...]}, {"errors":{"field":"msg"}}, or {"errors":"msg"}.
    private static IReadOnlyList<string> ParseErrors(string body)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return messages;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("errors", out var errors))
            {
                return messages;
            }

            switch (errors.ValueKind)
            {
                case JsonValueKind.Array:
                    messages.AddRange(errors.EnumerateArray().Select(e => e.ToString()));
                    break;
                case JsonValueKind.Object:
                    messages.AddRange(errors.EnumerateObject().Select(p => $"{p.Name}: {p.Value}"));
                    break;
                case JsonValueKind.String:
                    messages.Add(errors.GetString() ?? string.Empty);
                    break;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body — nothing structured to extract.
        }

        return messages.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
    }
}
