using System;
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

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> implemented against the Maxio Advanced Billing
/// REST API over a typed <see cref="HttpClient"/> (auth and base address are configured on
/// the client in DI). Maxio is the system of record; this service reads/writes it and never
/// persists subscription state locally.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // Plans in this integration require no payment method, so subscriptions are billed by
    // remittance (manual invoice) and activate immediately without card capture / 3-DS.
    private const string RemittancePaymentCollection = "remittance";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        _settings.Validate();
        var products = await GetProductsAsync(cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle, CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        // 1. Resolve the target plan against the live catalog (validates the handle and lets
        //    us fall back to a sensible default when the caller does not name a plan).
        var plan = await ResolvePlanAsync(planHandle, cancellationToken);

        // 2. Deterministic, per-(user, plan) reference. Maxio enforces reference uniqueness,
        //    which makes concurrent double-clicks race-safe: at most one subscription wins.
        var canonicalReference = BuildSubscriptionReference(subscriber.Reference, plan.Handle);

        // 3. Idempotent fast path: if a *live* enrolment for this plan already exists, return
        //    it (this is what makes a double-click a no-op).
        var existing = await FindSubscriptionByReferenceAsync(canonicalReference, cancellationToken);
        if (existing is not null && !IsTerminalState(existing.State))
        {
            _logger.LogInformation("Returning existing {State} subscription {SubscriptionId} for reference {Reference}.", existing.State, existing.Id, canonicalReference);
            return MapSubscription(existing);
        }

        // The canonical reference is free, or is held only by a terminated subscription. In the
        // latter case use a fresh unique reference so the shopper can re-subscribe to the plan.
        var subscriptionReference = existing is null
            ? canonicalReference
            : $"{canonicalReference}:{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

        // 4. Find-or-create the Maxio customer for this eShop user (idempotent by reference).
        await EnsureCustomerAsync(subscriber, cancellationToken);

        // 5. Create the subscription.
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioSubscriptionAttributes
            {
                ProductHandle = plan.Handle,
                CustomerReference = subscriber.Reference,
                PaymentCollectionMethod = RemittancePaymentCollection,
                Reference = subscriptionReference
            }
        };

        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request may have created this exact reference between steps 3 and 5.
            // Confirm that is the cause, then return the winner instead of surfacing an error.
            var errors = await ReadErrorsAsync(response, cancellationToken);
            if (errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase) && e.Contains("unique", StringComparison.OrdinalIgnoreCase)))
            {
                var raced = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (raced is not null)
                {
                    _logger.LogInformation("Lost a create race for reference {Reference}; returning the existing subscription {SubscriptionId}.", subscriptionReference, raced.Id);
                    return MapSubscription(raced);
                }
            }

            throw new BillingGatewayException($"Maxio rejected the subscription: {string.Join("; ", errors)}");
        }

        await EnsureSuccessAsync(response, "create subscription", cancellationToken);

        var created = await ReadEnvelopeAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        if (created?.Subscription is null)
        {
            throw new BillingGatewayException("Maxio returned an empty subscription payload.");
        }

        _logger.LogInformation("Created subscription {SubscriptionId} ({State}) for reference {Reference}.", created.Subscription.Id, created.Subscription.State, subscriptionReference);
        return MapSubscription(created.Subscription);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForUserAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        _settings.Validate();
        var customer = await LookupCustomerAsync(subscriber.Reference, cancellationToken);
        if (customer is null)
        {
            // The user has never enrolled, so no billing-system customer exists yet.
            return Array.Empty<CustomerSubscription>();
        }

        using var response = await SendAsync(HttpMethod.Get, $"customers/{customer.Id}/subscriptions.json", null, cancellationToken);
        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);

        var envelopes = await ReadEnvelopeAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken) ?? new List<MaxioSubscriptionEnvelope>();
        return envelopes
            .Where(e => e.Subscription is not null)
            .Select(e => MapSubscription(e.Subscription!))
            .ToList();
    }

    // ----- Maxio operations -----

    private async Task<IReadOnlyList<MaxioProduct>> GetProductsAsync(CancellationToken cancellationToken)
    {
        // The product-family path segment accepts "handle:{handle}" in place of a numeric id.
        var familyHandle = Uri.EscapeDataString(_settings.ProductFamilyHandle!);
        using var response = await SendAsync(HttpMethod.Get, $"product_families/handle:{familyHandle}/products.json", null, cancellationToken);
        await EnsureSuccessAsync(response, "list products", cancellationToken);

        var envelopes = await ReadEnvelopeAsync<List<MaxioProductEnvelope>>(response, cancellationToken) ?? new List<MaxioProductEnvelope>();
        return envelopes.Where(e => e.Product is not null).Select(e => e.Product!).ToList();
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string? planHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        if (plans.Count == 0)
        {
            throw new BillingGatewayException("The configured Maxio product family exposes no subscribable plans.");
        }

        if (!string.IsNullOrWhiteSpace(planHandle))
        {
            var match = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new SubscriptionPlanNotFoundException(planHandle);
            }

            return match;
        }

        // No plan named: default to the premium (highest-priced) plan in the family.
        return plans.OrderByDescending(p => p.PriceInCents).First();
    }

    private async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "lookup subscription", cancellationToken);
        var envelope = await ReadEnvelopeAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription;
    }

    private async Task EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return;
        }

        var (firstName, lastName) = DeriveName(subscriber);
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference
            }
        };

        using var response = await SendAsync(HttpMethod.Post, "customers.json", request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Another concurrent enrolment may have created the customer first; if the
            // reference is now taken, treat it as success and continue.
            var errors = await ReadErrorsAsync(response, cancellationToken);
            if (errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase) && e.Contains("unique", StringComparison.OrdinalIgnoreCase))
                && await LookupCustomerAsync(subscriber.Reference, cancellationToken) is not null)
            {
                return;
            }

            throw new BillingGatewayException($"Maxio rejected the customer: {string.Join("; ", errors)}");
        }

        await EnsureSuccessAsync(response, "create customer", cancellationToken);
        _logger.LogInformation("Created Maxio customer for reference {Reference}.", subscriber.Reference);
    }

    private async Task<MaxioCustomer?> LookupCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "lookup customer", cancellationToken);
        var envelope = await ReadEnvelopeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    // ----- HTTP plumbing -----

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativePath, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout (not a caller cancellation).
            throw new BillingGatewayException($"The Maxio billing system did not respond in time for '{method} {relativePath}'.");
        }
        catch (HttpRequestException ex)
        {
            throw new BillingGatewayException($"Could not reach the Maxio billing system for '{method} {relativePath}'.", ex);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = await ReadErrorsAsync(response, cancellationToken);
        var detail = errors.Count > 0 ? string.Join("; ", errors) : response.ReasonPhrase;
        throw new BillingGatewayException($"Maxio '{operation}' failed ({(int)response.StatusCode} {response.ReasonPhrase}): {detail}");
    }

    private static async Task<T?> ReadEnvelopeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new BillingGatewayException("Maxio returned a response that could not be parsed.", ex);
        }
    }

    private static async Task<List<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<MaxioErrorResponse>(JsonOptions, cancellationToken);
            return error?.Errors ?? new List<string>();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }

    // ----- mapping & helpers -----

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        Currency = subscription.Currency ?? "USD",
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        CurrentPeriodStartsAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        Reference = subscription.Reference,
        CustomerReference = subscription.Customer?.Reference
    };

    private static string BuildSubscriptionReference(string customerReference, string planHandle)
        => $"{customerReference}:{planHandle}";

    // Terminal states: the subscription is over and the plan may be subscribed to afresh.
    // Any other state (active, trialing, past_due, on_hold, awaiting_signup, ...) counts as a
    // live enrolment that a repeated subscribe call should return rather than duplicate.
    private static readonly string[] TerminalStates = { "canceled", "cancelled", "expired", "trial_ended" };

    private static bool IsTerminalState(string? state)
        => state is not null && Array.Exists(TerminalStates, s => s.Equals(state, StringComparison.OrdinalIgnoreCase));

    private static (string FirstName, string LastName) DeriveName(SubscriberIdentity subscriber)
    {
        if (!string.IsNullOrWhiteSpace(subscriber.FirstName) || !string.IsNullOrWhiteSpace(subscriber.LastName))
        {
            return (subscriber.FirstName ?? "eShopOnWeb", subscriber.LastName ?? "Subscriber");
        }

        // Maxio requires first and last name; derive a readable placeholder from the email
        // local-part when the identity carries no explicit name.
        var localPart = subscriber.Email.Split('@')[0];
        var first = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;
        return (first, "eShopOnWeb Subscriber");
    }
}
