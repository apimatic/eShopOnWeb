using System;
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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing (the system of record).
/// Idempotency is anchored in Maxio: the eShop user id is the customer <c>reference</c> (site-unique), and a
/// deterministic subscription <c>reference</c> lets a repeated subscribe reconcile to the existing record
/// rather than create a duplicate. No local persistence is used.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>Whole-operation deadline. The SDK's Timeout is per-attempt; this bounds a full flow end to end.</summary>
    private static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(30);

    /// <summary>Safety cap so plan pagination can never spin forever if the provider keeps returning pages.</summary>
    private const int MaxPlanPages = 20;
    private const int PlanPageSize = 100;

    /// <summary>Default collection method for card-less enrollment (Relationship Invoicing invoices, no card capture).</summary>
    private const string DefaultCollectionMethod = "remittance";

    // Maxio subscription states that mean the customer currently has access (see SubscriptionState remarks).
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        SubscriptionState.Active.Value,
        SubscriptionState.Trialing.Value,
        SubscriptionState.AwaitingSignup.Value,
        SubscriptionState.Assessing.Value,
        SubscriptionState.Pending.Value,
        SubscriptionState.Paused.Value,
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken ct = default)
    {
        using var scope = CreateBudgetScope(ct);
        var token = scope.Token;

        var familyId = await ResolveProductFamilyIdAsync(token);

        var plans = new List<SubscriptionPlan>();
        var page = 1;
        while (true)
        {
            IReadOnlyList<ProductResponse> pageProducts;
            try
            {
                pageProducts = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PlanPageSize,
                    ct: token);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _)) // 404 — family id no longer resolves
                {
                    throw new SubscriptionBillingException(
                        "The configured subscription product family could not be found.", 502, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw ToBillingException(raw, ex);
                }
                throw new SubscriptionBillingException("Unable to load subscription plans.", 502, ex);
            }
            catch (Exception ex) when (IsTransportFailure(ex, token))
            {
                throw new SubscriptionBillingException("The billing provider is currently unavailable.", 502, ex);
            }
            catch (JsonException ex)
            {
                throw new SubscriptionBillingException(
                    "The billing provider returned a response that could not be processed.", 502, ex);
            }

            foreach (var product in pageProducts.Select(p => p.Product))
            {
                if (product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }
                plans.Add(MapPlan(product));
            }

            if (pageProducts.Count < PlanPageSize)
            {
                break; // last page
            }
            if (++page > MaxPlanPages)
            {
                // Never return a silently-truncated plan list — a catalogue this large is abnormal.
                throw new SubscriptionBillingException("Too many subscription plans to list safely.", 502);
            }
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle, CancellationToken ct = default)
    {
        if (subscriber is null)
        {
            throw new ArgumentNullException(nameof(subscriber));
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionBillingException(
                "A plan handle is required. Choose one from GET /api/subscription-plans.", 400);
        }

        using var scope = CreateBudgetScope(ct);
        var token = scope.Token;

        // Validate the requested plan against the live catalogue for the configured family (cross-operation
        // invariant: a subscribe target must be one of the plans list-plans returns).
        var plans = await GetPlansForFamilyAsync(token);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionBillingException(
                $"Unknown plan '{planHandle}'. Choose one from GET /api/subscription-plans.", 400);
        }

        var customer = await EnsureCustomerAsync(subscriber, token);
        var customerId = customer.Id
            ?? throw new SubscriptionBillingException("The billing provider returned a customer without an id.", 502);

        var reference = BuildSubscriptionReference(subscriber.UserId);

        // Idempotency pre-check: a repeat subscribe (double-click, retry) returns the existing live subscription.
        var existing = await FindSubscriptionByReferenceAsync(reference, token);
        if (existing is not null && IsLive(existing.State))
        {
            _logger.LogInformation(
                "Subscribe is a no-op: user {UserId} already has live subscription {SubscriptionId} ({State}).",
                subscriber.UserId, existing.Id, existing.State?.Value);
            return new SubscribeResult
            {
                Subscription = MapSubscription(existing),
                CustomerId = customerId,
                AlreadySubscribed = true,
            };
        }

        var collectionMethod = string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod)
            ? DefaultCollectionMethod
            : _settings.PaymentCollectionMethod!;

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = plan.Handle,
                CustomerId = customerId,
                Reference = reference,
                // Enroll on an invoice basis so plans that need no payment method subscribe without card capture.
                PaymentCollectionMethod = CollectionMethod.FromValue(collectionMethod),
            },
        };

        Subscription created;
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct: token);
            created = response.Subscription
                ?? throw new SubscriptionBillingException("The billing provider returned an empty subscription.", 502);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // 422 may be a duplicate-reference race — reconcile before treating it as a failure.
            var reconciled = await FindSubscriptionByReferenceAsync(reference, token);
            if (reconciled is not null && IsLive(reconciled.State))
            {
                _logger.LogWarning(
                    "CreateSubscription returned an error but a live subscription {SubscriptionId} exists for user {UserId}; treating as idempotent success.",
                    reconciled.Id, subscriber.UserId);
                return new SubscribeResult
                {
                    Subscription = MapSubscription(reconciled),
                    CustomerId = customerId,
                    AlreadySubscribed = true,
                };
            }

            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                var message = errors.Errors.Count > 0
                    ? string.Join("; ", errors.Errors)
                    : "The subscription request was rejected by the billing provider.";
                _logger.LogWarning("CreateSubscription rejected for user {UserId}: {Message}", subscriber.UserId, message);
                throw new SubscriptionBillingException(message, 422, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToBillingException(raw, ex);
            }
            throw new SubscriptionBillingException("The subscription could not be created.", 502, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, token))
        {
            // Unknown outcome: the request may have reached Maxio. Re-read by reference before reporting failure.
            var reconciled = await TryReconcileAfterTransportFailureAsync(reference, token);
            if (reconciled is not null)
            {
                return new SubscribeResult
                {
                    Subscription = MapSubscription(reconciled),
                    CustomerId = customerId,
                    AlreadySubscribed = true,
                };
            }
            throw new SubscriptionBillingException("The billing provider is currently unavailable.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException(
                "The billing provider returned a response that could not be processed.", 502, ex);
        }

        _logger.LogInformation(
            "Created subscription {SubscriptionId} ({State}) for user {UserId} on plan {PlanHandle}.",
            created.Id, created.State?.Value, subscriber.UserId, plan.Handle);

        return new SubscribeResult
        {
            Subscription = MapSubscription(created),
            CustomerId = customerId,
            AlreadySubscribed = false,
        };
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken ct = default)
    {
        if (subscriber is null)
        {
            throw new ArgumentNullException(nameof(subscriber));
        }

        using var scope = CreateBudgetScope(ct);
        var token = scope.Token;

        var customer = await ReadCustomerByReferenceAsync(subscriber.UserId, token);
        if (customer?.Id is not int customerId)
        {
            return Array.Empty<CustomerSubscription>();
        }

        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct: token);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToBillingException(ex.Error, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, token))
        {
            throw new SubscriptionBillingException("The billing provider is currently unavailable.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException(
                "The billing provider returned a response that could not be processed.", 502, ex);
        }

        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    // --- customer ---

    private async Task<Customer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken ct)
    {
        var existing = await ReadCustomerByReferenceAsync(subscriber.UserId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = subscriber.UserId,
            },
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body, ct: ct);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.",
                response.Customer.Id, subscriber.UserId);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // 422 is almost certainly a duplicate reference from a concurrent create — the reference is
            // site-unique, so re-read and use the winning record.
            var raced = await ReadCustomerByReferenceAsync(subscriber.UserId, ct);
            if (raced is not null)
            {
                _logger.LogWarning("Customer create raced for user {UserId}; using existing customer {CustomerId}.",
                    subscriber.UserId, raced.Id);
                return raced;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new SubscriptionBillingException("The customer details were rejected by the billing provider.", 422, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToBillingException(raw, ex);
            }
            throw new SubscriptionBillingException("The billing customer could not be created.", 502, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            // Unknown outcome: the create may have landed. Re-read by reference before reporting failure.
            var raced = await ReadCustomerByReferenceAsync(subscriber.UserId, ct);
            if (raced is not null)
            {
                return raced;
            }
            throw new SubscriptionBillingException("The billing provider is currently unavailable.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException(
                "The billing provider returned a response that could not be processed.", 502, ex);
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ToBillingException(ex.Error, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw new SubscriptionBillingException("The billing provider is currently unavailable.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException(
                "The billing provider returned a response that could not be processed.", 502, ex);
        }
    }

    // --- subscription lookup ---

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference, ct: ct);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _)) // 404 — no subscription with this reference
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                if (raw.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }
                throw ToBillingException(raw, ex);
            }
            throw new SubscriptionBillingException("Unable to look up the subscription.", 502, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw new SubscriptionBillingException("The billing provider is currently unavailable.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException(
                "The billing provider returned a response that could not be processed.", 502, ex);
        }
    }

    /// <summary>
    /// Best-effort re-read after a transport failure on a write, where the request may already have landed.
    /// Swallows a second failure (the outcome is genuinely unknown) and returns null.
    /// </summary>
    private async Task<Subscription?> TryReconcileAfterTransportFailureAsync(string reference, CancellationToken ct)
    {
        try
        {
            var found = await FindSubscriptionByReferenceAsync(reference, ct);
            return found is not null && IsLive(found.State) ? found : null;
        }
        catch (SubscriptionBillingException)
        {
            return null;
        }
    }

    // --- product family / plans ---

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct)
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
            throw ToBillingException(ex.Error, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw new SubscriptionBillingException("The billing provider is currently unavailable.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException(
                "The billing provider returned a response that could not be processed.", 502, ex);
        }

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null
                && string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family?.Id is not int id)
        {
            throw new SubscriptionBillingException(
                "The configured subscription product family could not be found.", 502);
        }
        return id;
    }

    /// <summary>Loads plans for the configured family using the caller's budget token (no new scope).</summary>
    private async Task<IReadOnlyList<SubscriptionPlan>> GetPlansForFamilyAsync(CancellationToken ct)
    {
        var familyId = await ResolveProductFamilyIdAsync(ct);
        var plans = new List<SubscriptionPlan>();
        var page = 1;
        while (true)
        {
            IReadOnlyList<ProductResponse> pageProducts;
            try
            {
                pageProducts = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PlanPageSize,
                    ct: ct);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw new SubscriptionBillingException(
                        "The configured subscription product family could not be found.", 502, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw ToBillingException(raw, ex);
                }
                throw new SubscriptionBillingException("Unable to load subscription plans.", 502, ex);
            }
            catch (Exception ex) when (IsTransportFailure(ex, ct))
            {
                throw new SubscriptionBillingException("The billing provider is currently unavailable.", 502, ex);
            }
            catch (JsonException ex)
            {
                throw new SubscriptionBillingException(
                    "The billing provider returned a response that could not be processed.", 502, ex);
            }

            foreach (var product in pageProducts.Select(p => p.Product))
            {
                if (product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }
                plans.Add(MapPlan(product));
            }

            if (pageProducts.Count < PlanPageSize)
            {
                break;
            }
            if (++page > MaxPlanPages)
            {
                throw new SubscriptionBillingException("Too many subscription plans to list safely.", 502);
            }
        }
        return plans;
    }

    // --- mapping ---

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value,
        RequiresPaymentMethod = product.RequireCreditCard ?? false,
    };

    private static CustomerSubscription MapSubscription(Subscription sub) => new()
    {
        Id = sub.Id ?? 0,
        PlanHandle = sub.Product?.Handle,
        PlanName = sub.Product?.Name,
        State = sub.State?.Value ?? "unknown",
        IsLive = IsLive(sub.State),
        PriceInCents = sub.ProductPriceInCents,
        NextBillingAt = sub.CurrentPeriodEndsAt ?? sub.NextAssessmentAt,
        Reference = sub.Reference,
    };

    // --- helpers ---

    private static bool IsLive(SubscriptionState? state) =>
        state?.Value is string value && LiveStates.Contains(value);

    private static bool IsLive(string? state) =>
        state is not null && LiveStates.Contains(state);

    private static string BuildSubscriptionReference(string userId) => $"eshopweb-{userId}";

    private CancellationTokenSource CreateBudgetScope(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(OperationBudget);
        return cts;
    }

    /// <summary>
    /// True for connection/timeout failures — but not when the caller cancelled (that is rethrown as-is by the
    /// filtered catch not matching, since we exclude it here).
    /// </summary>
    private static bool IsTransportFailure(Exception ex, CancellationToken linkedToken)
    {
        if (ex is HttpRequestException)
        {
            return true;
        }
        if (ex is TaskCanceledException or OperationCanceledException)
        {
            // A cancellation that is NOT the SDK/our-budget deadline is the caller aborting; let it propagate.
            return linkedToken.IsCancellationRequested;
        }
        return false;
    }

    private SubscriptionBillingException ToBillingException(RawError raw, Exception inner)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("Maxio returned HTTP {Status} for a subscription operation.", status);

        // Our credentials/quota problems are not the caller's fault → 502/503.
        if (status is 401 or 403)
        {
            return new SubscriptionBillingException("The billing provider rejected our credentials.", 502, inner);
        }
        if (status == 429)
        {
            return new SubscriptionBillingException("The billing provider is rate limiting requests. Please retry shortly.", 503, inner);
        }
        // Caller-actionable 4xx pass through with the same status.
        if (status is >= 400 and < 500)
        {
            return new SubscriptionBillingException("The billing request was rejected.", status, inner);
        }
        // 5xx and anything else: provider-side.
        return new SubscriptionBillingException("The billing provider is currently unavailable.", 502, inner);
    }
}
