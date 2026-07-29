using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>. Every provider
/// failure is translated into a <see cref="SubscriptionBillingException"/> carrying a caller-safe message
/// and an HTTP status, so no SDK/JSON internal ever leaks past this boundary.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Non-terminal states: a subscription in one of these is "live" and must not be duplicated.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        MaxioSettings settings,
        IMemoryCache cache,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        try
        {
            var responses = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 200,
                ct: cancellationToken);

            var plans = new List<SubscriptionPlan>();
            foreach (var response in responses)
            {
                var product = response.Product;
                if (product is null || string.IsNullOrEmpty(product.Handle))
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    Handle: product.Handle!,
                    Name: string.IsNullOrEmpty(product.Name) ? product.Handle! : product.Name!,
                    Description: product.Description,
                    PriceInCents: product.PriceInCents ?? 0,
                    Interval: product.Interval ?? 0,
                    IntervalUnit: product.IntervalUnit?.Value ?? string.Empty,
                    ProductId: product.Id ?? 0));
            }

            return plans;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw TranslateListProducts(ex);
        }
        catch (Exception ex) when (IsProviderUnreachable(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw MalformedBody(ex);
        }
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionBillingException("A plan handle is required to subscribe.", HttpStatusCode.BadRequest);
        }

        // Step 1 — ensure exactly one Maxio customer exists for this eShop user (idempotent).
        var customerId = await EnsureCustomerAsync(subscriber, cancellationToken);

        // Idempotency for the subscription itself: if the customer already holds a live subscription
        // to this plan, return it instead of creating a duplicate (covers double-click / retries).
        var existing = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var response = await CreateSubscriptionWithoutPaymentMethodAsync(customerId, planHandle, cancellationToken);

            var subscription = response.Subscription
                ?? throw new SubscriptionBillingException("The billing provider returned an empty subscription.", HttpStatusCode.BadGateway);

            return MapSubscription(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw TranslateCreateSubscription(ex);
        }
        catch (Exception ex) when (IsProviderUnreachable(ex, cancellationToken))
        {
            // A transport failure is retried by the SDK on any verb, so the POST may already have
            // enrolled the customer. Reconcile by re-reading before reporting failure.
            var reconciled = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
            if (reconciled is not null)
            {
                _logger.LogWarning("Maxio create-subscription transport error reconciled to existing subscription {SubscriptionId}.", reconciled.SubscriptionId);
                return reconciled;
            }

            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw MalformedBody(ex);
        }
    }

    /// <summary>
    /// Creates the subscription without capturing a payment method. The seeded plans do not require a
    /// card, but the site still attempts automatic collection at signup unless the subscription uses an
    /// invoice-style collection method. "Remittance" is correct on the current Relationship-Invoicing
    /// architecture and "Invoice" on the legacy Statements architecture; we prefer Remittance and fall
    /// back to Invoice only when the provider rejects it for a collection/payment reason.
    /// </summary>
    private async Task<SubscriptionResponse> CreateSubscriptionWithoutPaymentMethodAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        try
        {
            return await CreateSubscriptionAsync(customerId, planHandle, CollectionMethod.Remittance, cancellationToken);
        }
        catch (SdkException<CreateSubscriptionError> ex) when (IndicatesCollectionMethodUnsupported(ex))
        {
            _logger.LogWarning("Maxio rejected remittance collection; retrying with invoice collection (legacy architecture).");
            return await CreateSubscriptionAsync(customerId, planHandle, CollectionMethod.Invoice, cancellationToken);
        }
    }

    private Task<SubscriptionResponse> CreateSubscriptionAsync(int customerId, string planHandle, CollectionMethod collectionMethod, CancellationToken cancellationToken) =>
        _client.Subscriptions.CreateSubscription(
            body: new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = planHandle,
                    PaymentCollectionMethod = collectionMethod
                }
            },
            ct: cancellationToken);

    private static bool IndicatesCollectionMethodUnsupported(SdkException<CreateSubscriptionError> ex)
    {
        if (!ex.Error.TryGetErrorListResponse1(out var body) || body?.Errors is not { Count: > 0 } errors)
        {
            return false;
        }

        // A payment/collection/balance complaint means the chosen collection method did not suppress
        // auto-collection on this site; a different 422 (e.g. an unknown plan handle) should not retry.
        return errors.Any(e => e is not null && (
            e.Contains("payment", StringComparison.OrdinalIgnoreCase)
            || e.Contains("collection", StringComparison.OrdinalIgnoreCase)
            || e.Contains("balance", StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        var customerId = await FindCustomerIdByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customerId is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await ListCustomerSubscriptionsAsync(customerId.Value, cancellationToken);
    }

    // --- customer ensure (idempotent lookup-or-create) ---

    private async Task<int> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerIdByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing.Value;
        }

        try
        {
            var response = await _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = subscriber.FirstName,
                        LastName = subscriber.LastName,
                        Email = subscriber.Email,
                        Reference = subscriber.Reference
                    }
                },
                ct: cancellationToken);

            var id = response.Customer?.Id
                ?? throw new SubscriptionBillingException("The billing provider returned a customer with no id.", HttpStatusCode.BadGateway);
            return id;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // The unique-reference constraint is the server-side backstop for a lookup/create race:
            // a duplicate-reference create is "already exists", so re-read rather than surfacing an error.
            var reread = await FindCustomerIdByReferenceAsync(subscriber.Reference, cancellationToken);
            if (reread is not null)
            {
                return reread.Value;
            }

            throw TranslateCreateCustomer(ex);
        }
        catch (Exception ex) when (IsProviderUnreachable(ex, cancellationToken))
        {
            // Transport failure: the create POST may already have landed. Reconcile before failing.
            var reread = await FindCustomerIdByReferenceAsync(subscriber.Reference, cancellationToken);
            if (reread is not null)
            {
                return reread.Value;
            }

            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw MalformedBody(ex);
        }
    }

    private async Task<int?> FindCustomerIdByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, cancellationToken);
            return response.Customer?.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // No customer with this reference yet.
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsProviderUnreachable(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw MalformedBody(ex);
        }
    }

    // --- product family / plans ---

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        var handle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new SubscriptionBillingException("The Maxio product family handle is not configured.", HttpStatusCode.InternalServerError);
        }

        var cacheKey = $"maxio:family-id:{handle}";
        if (_cache.TryGetValue(cacheKey, out int cachedId))
        {
            return cachedId;
        }

        try
        {
            var families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: cancellationToken);

            foreach (var response in families)
            {
                var family = response.ProductFamily;
                if (family?.Id is int id && string.Equals(family.Handle, handle, StringComparison.Ordinal))
                {
                    _cache.Set(cacheKey, id, TimeSpan.FromMinutes(30));
                    return id;
                }
            }

            throw new SubscriptionBillingException($"No Maxio product family found with handle '{handle}'.", HttpStatusCode.NotFound);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsProviderUnreachable(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw MalformedBody(ex);
        }
    }

    // --- subscriptions ---

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase)
            && !TerminalStates.Contains(s.State));
    }

    private async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(customerId, cancellationToken);

            var subscriptions = new List<CustomerSubscription>();
            foreach (var response in responses)
            {
                if (response.Subscription is { } subscription)
                {
                    subscriptions.Add(MapSubscription(subscription));
                }
            }

            return subscriptions;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsProviderUnreachable(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw MalformedBody(ex);
        }
    }

    private static CustomerSubscription MapSubscription(Subscription subscription) => new(
        SubscriptionId: subscription.Id ?? 0,
        State: subscription.State?.Value ?? "unknown",
        PlanHandle: subscription.Product?.Handle,
        PlanName: subscription.Product?.Name,
        PriceInCents: subscription.ProductPriceInCents ?? 0,
        CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt);

    // --- error translation (single boundary → SubscriptionBillingException) ---

    private SubscriptionBillingException TranslateCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            // 422: the customer payload was rejected. The nested error body is provider-internal, so
            // surface a controlled, caller-safe message rather than echoing it.
            _logger.LogWarning("Maxio rejected customer create (422).");
            return new SubscriptionBillingException("The billing provider rejected the customer details.", HttpStatusCode.UnprocessableEntity, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return Translate(raw, ex);
        }

        return new SubscriptionBillingException("The billing provider rejected the request.", HttpStatusCode.BadGateway, ex);
    }

    private SubscriptionBillingException TranslateCreateSubscription(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var body) && body?.Errors is { Count: > 0 } errors)
        {
            // 422 validation: these messages describe what the caller sent (e.g. an unknown plan handle),
            // so they are safe to return.
            return new SubscriptionBillingException(string.Join("; ", errors), HttpStatusCode.UnprocessableEntity, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return Translate(raw, ex);
        }

        return new SubscriptionBillingException("The billing provider rejected the subscription request.", HttpStatusCode.BadGateway, ex);
    }

    private SubscriptionBillingException TranslateListProducts(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out _))
        {
            // 404: the product family id was not found at the provider.
            return new SubscriptionBillingException("The configured product family could not be found at the billing provider.", HttpStatusCode.NotFound, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return Translate(raw, ex);
        }

        return new SubscriptionBillingException("The billing provider could not list plans.", HttpStatusCode.BadGateway, ex);
    }

    private SubscriptionBillingException Translate(SdkException<RawError> ex) => Translate(ex.Error, ex);

    private SubscriptionBillingException Translate(RawError raw, Exception source)
    {
        var clientStatus = MapProviderStatus(raw.StatusCode);
        _logger.LogWarning("Maxio call failed with provider status {ProviderStatus} → surfacing {ClientStatus}.",
            (int)raw.StatusCode, (int)clientStatus);
        return new SubscriptionBillingException(SafeMessageFor(raw.StatusCode), clientStatus, source);
    }

    /// <summary>
    /// Maps a provider HTTP status onto the status this boundary returns: a caller-actionable 4xx passes
    /// through; our own auth/other failures are hidden behind a 5xx so a retrying caller is not told to
    /// fix something on their end that they cannot.
    /// </summary>
    private static HttpStatusCode MapProviderStatus(HttpStatusCode providerStatus)
    {
        int code = (int)providerStatus;
        return code switch
        {
            401 or 403 => HttpStatusCode.BadGateway,        // our credentials, not the caller's problem
            404 => HttpStatusCode.NotFound,
            409 => HttpStatusCode.Conflict,
            422 => HttpStatusCode.UnprocessableEntity,
            429 => HttpStatusCode.ServiceUnavailable,
            >= 400 and < 500 => HttpStatusCode.BadGateway,  // other 4xx: not caller-actionable
            _ => HttpStatusCode.BadGateway
        };
    }

    private static string SafeMessageFor(HttpStatusCode providerStatus) => (int)providerStatus switch
    {
        401 or 403 => "The billing provider rejected our credentials.",
        404 => "The requested billing resource was not found.",
        409 => "The request conflicts with the current billing state.",
        422 => "The billing provider rejected the request.",
        429 => "The billing provider is throttling requests. Please try again shortly.",
        >= 500 => "The billing provider is currently unavailable.",
        _ => "The billing provider returned an unexpected response."
    };

    private SubscriptionBillingException Unreachable(Exception ex)
    {
        _logger.LogWarning("Maxio provider unreachable: {Error}.", ex.Message);
        return new SubscriptionBillingException("The billing provider is currently unreachable. Please try again shortly.", HttpStatusCode.ServiceUnavailable, ex);
    }

    private SubscriptionBillingException MalformedBody(JsonException ex)
    {
        // A JsonException reaches this boundary from a drifted success body OR an error body that did
        // not match its generated error model (status already lost by then). Either way the outcome is
        // genuinely unknown, so surface a controlled 5xx — never a domain "absence".
        _logger.LogWarning("Maxio returned a response that could not be processed: {Error}.", ex.Message);
        return new SubscriptionBillingException("The billing provider returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
    }

    /// <summary>
    /// True when the failure is a provider-connectivity failure (unreachable host, dropped socket, or a
    /// timeout) rather than a caller-initiated cancellation — which must propagate untouched.
    /// </summary>
    private static bool IsProviderUnreachable(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException
        || (ex is TaskCanceledException or OperationCanceledException && !cancellationToken.IsCancellationRequested);
}
