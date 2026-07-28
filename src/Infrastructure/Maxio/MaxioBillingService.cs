using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="IMaxioBillingService"/>. This is the only
/// place the Maxio SDK is referenced; every SDK type and failure is mapped onto eShopOnWeb's own
/// domain shapes and <see cref="BillingException"/> before it leaves this class.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    private const int PlansPageSize = 100;

    // Lifecycle states in which a subscription no longer counts as an active enrollment for dedup.
    private static readonly HashSet<string> TerminalStates =
        new(StringComparer.OrdinalIgnoreCase) { "canceled", "expired", "failed_to_create" };

    // Serializes concurrent subscribe calls for the same user (e.g. a double-click) regardless of DI lifetime.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        var products = new List<Product>();
        var page = 1;
        while (true)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: null,
                    include: null,
                    page: page,
                    perPage: PlansPageSize,
                    ct: cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFound))
                {
                    throw new BillingException(
                        $"Maxio product family '{_settings.ProductFamilyHandle}' was not found: {notFound}",
                        (int)HttpStatusCode.NotFound, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new BillingException(
                        $"Maxio failed to list products (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode, ex);
                }
                throw new BillingException("Maxio failed to list products.", ex);
            }
            catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
            {
                throw new BillingException("Maxio is unreachable while listing products.", ex);
            }
            catch (JsonException ex)
            {
                throw new BillingException("Maxio returned a products response that could not be processed.", ex);
            }

            foreach (var item in pageItems)
            {
                products.Add(item.Product);
            }

            if (pageItems.Count < PlansPageSize)
            {
                break;
            }
            page++;
        }

        var plans = products
            .Select(MapPlan)
            .OrderBy(p => p.PriceInCents)
            .ToList();

        _logger.LogInformation(
            "Listed {PlanCount} Maxio plan(s) for product family {ProductFamilyHandle}.",
            plans.Count, _settings.ProductFamilyHandle);

        return plans;
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        string userReference, CancellationToken cancellationToken = default)
    {
        var customer = await TryReadCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer?.Id is not int customerId)
        {
            _logger.LogInformation(
                "No Maxio customer exists for reference {UserReference}; returning no subscriptions.", userReference);
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        var mapped = subscriptions.Select(MapSubscription).ToList();

        _logger.LogInformation(
            "Listed {SubscriptionCount} Maxio subscription(s) for reference {UserReference}.",
            mapped.Count, userReference);

        return mapped;
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var gate = UserLocks.GetOrAdd(request.UserReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(request, cancellationToken);
            if (customer.Id is not int customerId)
            {
                throw new BillingException("Maxio returned a customer without an id.");
            }

            // Dedup: an existing, non-terminal subscription to the same plan is an idempotent replay.
            var existing = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
            var active = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase)
                && !IsTerminal(s.State?.Value));
            if (active is not null)
            {
                _logger.LogInformation(
                    "Reusing existing Maxio subscription {SubscriptionId} for reference {UserReference} plan {PlanHandle}.",
                    active.Id, request.UserReference, request.PlanHandle);
                return new SubscribeResult { Subscription = MapSubscription(active), Created = false };
            }

            var created = await CreateSubscriptionAsync(request, customerId, cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for reference {UserReference} plan {PlanHandle}.",
                created.Id, request.UserReference, request.PlanHandle);

            return new SubscribeResult { Subscription = created, Created = true };
        }
        finally
        {
            gate.Release();
        }
    }

    // ---- customer ensure (find-or-create, idempotent by reference) ----

    private async Task<Customer> EnsureCustomerAsync(SubscribeRequest request, CancellationToken ct)
    {
        var existing = await TryReadCustomerByReferenceAsync(request.UserReference, ct);
        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = string.IsNullOrWhiteSpace(request.FirstName)
                    ? EmailLocalPart(request.Email)
                    : request.FirstName!,
                LastName = string.IsNullOrWhiteSpace(request.LastName) ? "eShopOnWeb" : request.LastName!,
                Email = request.Email,
                Reference = request.UserReference,
            },
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body, ct);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A 422 may be a concurrent duplicate reference — the winner already exists.
            var winner = await TryReadCustomerByReferenceAsync(request.UserReference, ct);
            if (winner is not null)
            {
                _logger.LogWarning(
                    "CreateCustomer for reference {UserReference} lost a race; using the existing customer.",
                    request.UserReference);
                return winner;
            }
            throw ToBillingExceptionFromCreateCustomer(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            // A POST can be resent by the SDK's transport retries, so the customer may already exist — reconcile.
            var winner = await TryReadCustomerByReferenceAsync(request.UserReference, ct);
            if (winner is not null)
            {
                return winner;
            }
            throw new BillingException("Maxio is unreachable while creating a customer.", ex);
        }
        catch (JsonException ex)
        {
            // Undeserializable 2xx, or an error body that did not match the generated shape — reconcile then surface.
            var winner = await TryReadCustomerByReferenceAsync(request.UserReference, ct);
            if (winner is not null)
            {
                return winner;
            }
            throw new BillingException("Maxio returned a customer response that could not be processed.", ex);
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // A find-miss is control flow, not an error.
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingException(
                $"Maxio failed to look up customer '{reference}' (HTTP {(int)ex.Error.StatusCode}).",
                (int)ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw new BillingException("Maxio is unreachable while looking up a customer.", ex);
        }
        catch (JsonException ex)
        {
            throw new BillingException("Maxio returned a customer response that could not be processed.", ex);
        }
    }

    // ---- subscription create ----

    // Collection methods that enroll WITHOUT a stored payment method (bill by invoice, not auto-charge).
    // "Remittance" is the value for a Relationship-Invoicing site; "Invoice" is the legacy-Statements
    // value. We try both so the same build works regardless of the target site's billing architecture.
    private static readonly CollectionMethod[] NoCardCollectionMethods =
        { CollectionMethod.Remittance, CollectionMethod.Invoice };

    private async Task<CustomerSubscription> CreateSubscriptionAsync(
        SubscribeRequest request, int customerId, CancellationToken ct)
    {
        // A default (automatic) subscription with a balance requires a card. These plans capture no
        // card, so we bill on invoice/remittance instead. A 422 means nothing was created, so if the
        // site rejects the first collection method we can safely retry with the alternate one.
        BillingException? lastValidationError = null;
        for (var attempt = 0; attempt < NoCardCollectionMethods.Length; attempt++)
        {
            var collectionMethod = NoCardCollectionMethods[attempt];
            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = request.PlanHandle,
                    CustomerId = customerId,
                    // No payment profile is created; invoice/remittance collection needs none.
                    PaymentCollectionMethod = collectionMethod,
                },
            };

            Subscription createdSubscription;
            try
            {
                var response = await _client.Subscriptions.CreateSubscription(body, ct);
                if (response.Subscription is null)
                {
                    throw new BillingException("Maxio returned an empty subscription response.");
                }
                createdSubscription = response.Subscription;
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                var translated = TranslateCreateSubscriptionError(ex, request, collectionMethod);
                // A 422 created nothing; if this collection method was rejected, try the alternate.
                if (translated.StatusCode == 422 && attempt < NoCardCollectionMethods.Length - 1)
                {
                    lastValidationError = translated;
                    _logger.LogWarning(
                        "Maxio rejected subscription for reference {UserReference} plan {PlanHandle} with collection method {CollectionMethod}; retrying with the alternate.",
                        request.UserReference, request.PlanHandle, collectionMethod.Value);
                    continue;
                }
                throw translated;
            }
            catch (Exception ex) when (IsTransportFailure(ex, ct))
            {
                // A POST can be resent by transport retries — the subscription may already exist. Reconcile.
                var reconciled = await FindActiveSubscriptionAsync(customerId, request.PlanHandle, ct);
                if (reconciled is not null)
                {
                    _logger.LogWarning(
                        "CreateSubscription for reference {UserReference} failed on transport but the subscription exists; reconciled.",
                        request.UserReference);
                    return reconciled;
                }
                throw new BillingException("Maxio is unreachable while creating a subscription.", ex);
            }
            catch (JsonException ex)
            {
                throw new BillingException("Maxio returned a subscription response that could not be processed.", ex);
            }

            // Re-fetch for complete/consistent data; fall back to the create response if the fetch doesn't include it yet.
            if (createdSubscription.Id is int id)
            {
                var all = await ListCustomerSubscriptionsAsync(customerId, ct);
                var match = all.FirstOrDefault(s => s.Id == id);
                if (match is not null)
                {
                    return MapSubscription(match);
                }
            }

            return MapSubscription(createdSubscription);
        }

        // Every collection method returned a 422 — surface the most recent validation error.
        throw lastValidationError ?? new BillingException("Maxio failed to create the subscription.", 422);
    }

    private BillingException TranslateCreateSubscriptionError(
        SdkException<CreateSubscriptionError> ex, SubscribeRequest request, CollectionMethod collectionMethod)
    {
        if (ex.Error.TryGetErrorListResponse1(out var errors))
        {
            var detail = errors.Errors is { Count: > 0 }
                ? string.Join("; ", errors.Errors)
                : "validation failed";
            _logger.LogWarning(
                "Maxio rejected subscription for reference {UserReference} plan {PlanHandle} ({CollectionMethod}): {Detail}",
                request.UserReference, request.PlanHandle, collectionMethod.Value, detail);
            return new BillingException($"Maxio rejected the subscription: {detail}", 422, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new BillingException(
                $"Maxio failed to create the subscription (HTTP {(int)raw.StatusCode}).",
                (int)raw.StatusCode, ex);
        }
        return new BillingException("Maxio failed to create the subscription.", ex);
    }

    private async Task<CustomerSubscription?> FindActiveSubscriptionAsync(
        int customerId, string planHandle, CancellationToken ct)
    {
        var all = await ListCustomerSubscriptionsAsync(customerId, ct);
        var match = all.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
            && !IsTerminal(s.State?.Value));
        return match is null ? null : MapSubscription(match);
    }

    // ---- shared reads ----

    private async Task<string> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingException(
                $"Maxio failed to list product families (HTTP {(int)ex.Error.StatusCode}).",
                (int)ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw new BillingException("Maxio is unreachable while listing product families.", ex);
        }
        catch (JsonException ex)
        {
            throw new BillingException("Maxio returned a product-families response that could not be processed.", ex);
        }

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null
                && string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family?.Id is not int familyId)
        {
            throw new BillingException(
                $"Maxio product family with handle '{_settings.ProductFamilyHandle}' was not found.",
                (int)HttpStatusCode.NotFound);
        }

        return familyId.ToString();
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(customerId, ct);
            return responses
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingException(
                $"Maxio failed to list subscriptions for customer {customerId} (HTTP {(int)ex.Error.StatusCode}).",
                (int)ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw new BillingException("Maxio is unreachable while listing subscriptions.", ex);
        }
        catch (JsonException ex)
        {
            throw new BillingException("Maxio returned a subscriptions response that could not be processed.", ex);
        }
    }

    // ---- mapping ----

    private SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Currency = "USD",
        Interval = product.IntervalUnit?.Value ?? "month",
        IntervalCount = product.Interval ?? 1,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? _settings.ProductFamilyHandle,
    };

    private static CustomerSubscription MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        Currency = "USD",
        State = subscription.State?.Value ?? string.Empty,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        // The subscription response carries no next_billing_at; next_assessment_at (falling back to
        // current_period_ends_at) is the display value.
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference ?? subscription.Reference,
    };

    // ---- helpers ----

    private static bool IsTerminal(string? state) => state is not null && TerminalStates.Contains(state);

    private static bool IsTransportFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested); // SDK per-attempt timeout, not caller cancel

    private static string EmailLocalPart(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email.Substring(0, at) : email;
        return string.IsNullOrWhiteSpace(local) ? "eShopOnWeb" : local;
    }

    /// <summary>
    /// Builds a <see cref="BillingException"/> from a CreateCustomer failure. UNVERIFIED: the generated
    /// <c>CustomerErrorResponse1.Errors</c> models only <c>per_page</c>/<c>price_point</c> — fields unrelated
    /// to customer validation — so real 422 messages are best-effort at most. Extract what the typed shape
    /// exposes, otherwise fall back to the raw body; if neither yields text, carry the 422 status alone.
    /// </summary>
    private static BillingException ToBillingExceptionFromCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var typed))
        {
            var messages = new List<string>();
            if (typed.Errors?.PerPage is { Count: > 0 } perPage)
            {
                messages.AddRange(perPage);
            }
            if (typed.Errors?.PricePoint is { Count: > 0 } pricePoint)
            {
                messages.AddRange(pricePoint);
            }
            if (messages.Count > 0)
            {
                return new BillingException($"Maxio rejected the customer: {string.Join("; ", messages)}", 422, ex);
            }
            return new BillingException("Maxio rejected the customer request (validation failed).", 422, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new BillingException(
                $"Maxio rejected the customer request (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}",
                (int)raw.StatusCode, ex);
        }
        return new BillingException("Maxio rejected the customer request.", ex);
    }
}
