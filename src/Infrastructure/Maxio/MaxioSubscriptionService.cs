using System;
using System.Collections.Concurrent;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing (system of record).
/// All provider/transport failures are translated to <see cref="BillingProviderException"/> so the
/// API boundary has a single failure type to map.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionBillingService
{
    // Total per-request budget across all SDK calls in one operation. SDK Timeout is per-attempt;
    // this CancellationToken deadline is the only thing that bounds a whole operation.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // Plan-list paging: high perPage (seeded catalog is tiny) with a safety cap that surfaces truncation.
    private const int PlanPageSize = 100;
    private const int MaxPlanPages = 20;

    // Maxio subscription states that count as a live enrollment for idempotency (a shopper in one of
    // these already has this plan; a canceled/expired one may re-subscribe). Wire values.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "pending", "assessing", "past_due", "soft_failure", "awaiting_signup"
    };

    // Per-user in-process serialization. Mitigation only (single host) — see maxio-advanced-billing-plan.md §6.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();

    // Resolved once per process: the configured family handle → its numeric id (the products-by-family
    // endpoint takes the numeric id, not the handle). Ids are stable within a run; a re-seed implies a restart.
    private readonly SemaphoreSlim _familyIdLock = new(1, 1);
    private int? _cachedFamilyId;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SubscriptionPlanList> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = CreateBudget(cancellationToken);
        return await GetPlansCoreAsync(budget.Token);
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        using var budget = CreateBudget(cancellationToken);
        var ct = budget.Token;

        // Cross-operation invariant: the requested handle must be one the catalog actually offers.
        var plans = await GetPlansCoreAsync(ct);
        if (plans.Plans.All(p => !string.Equals(p.Handle, request.PlanHandle, StringComparison.Ordinal)))
        {
            throw new BillingProviderException(
                $"Unknown subscription plan handle '{request.PlanHandle}'.",
                HttpStatusCode.BadRequest, isCallerError: true);
        }

        var subscriptionReference = BuildSubscriptionReference(request.UserReference, request.PlanHandle);

        // Serialize a single user's concurrent subscribe calls on this host (double-click mitigation).
        var gate = _userLocks.GetOrAdd(request.UserReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var customer = await EnsureCustomerAsync(request, ct);
            var customerId = customer.Id
                ?? throw new BillingProviderException("Maxio returned a customer without an id.", HttpStatusCode.BadGateway);

            // Existence check: an existing live subscription to the same plan is returned, not duplicated.
            var existing = await FindExistingLiveSubscriptionAsync(customerId, request.PlanHandle, ct);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Subscribe is a no-op: customer {CustomerId} already has a live subscription {SubscriptionId} to plan {PlanHandle}.",
                    customerId, existing.Id, request.PlanHandle);
                return new SubscribeResult { Subscription = existing, AlreadyExisted = true };
            }

            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = request.PlanHandle,
                    Reference = subscriptionReference,
                    // Invoice-based collection so a shopper can subscribe without capturing a card:
                    // the plans require no payment method, and 'automatic' would try to charge the
                    // full balance at signup and fail ("no payment method on file").
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            };

            SubscriptionResponse created;
            try
            {
                created = await _client.Subscriptions.CreateSubscription(body: body, ct: ct);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errors))
                {
                    throw new BillingProviderException(
                        DescribeErrors("Subscription could not be created", errors.Errors),
                        HttpStatusCode.UnprocessableEntity, isCallerError: true, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw Translate("create subscription", raw, ex);
                }
                throw new BillingProviderException("Subscription could not be created (unrecognised error shape).", HttpStatusCode.BadGateway, innerException: ex);
            }
            catch (Exception ex) when (IsTransportFault(ex, cancellationToken))
            {
                // Unknown outcome: the create may have landed. Reconcile by the deterministic reference.
                _logger.LogWarning(ex, "Transport fault creating subscription {Reference}; reconciling.", subscriptionReference);
                var reconciled = await TryFindSubscriptionAsync(subscriptionReference, ct);
                if (reconciled is not null)
                {
                    return new SubscribeResult { Subscription = reconciled, AlreadyExisted = true };
                }
                throw new BillingProviderException("The billing provider could not be reached.", HttpStatusCode.BadGateway, innerException: ex);
            }
            catch (JsonException ex)
            {
                throw new BillingProviderException("The billing provider returned a response that could not be processed.", HttpStatusCode.BadGateway, innerException: ex);
            }

            var subscription = created.Subscription
                ?? throw new BillingProviderException("Maxio returned an empty subscription.", HttpStatusCode.BadGateway);

            var mapped = MapSubscription(subscription);
            _logger.LogInformation(
                "Created subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle} in state {State}.",
                mapped.Id, customerId, request.PlanHandle, mapped.State);
            return new SubscribeResult { Subscription = mapped, AlreadyExisted = false };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        using var budget = CreateBudget(cancellationToken);
        var ct = budget.Token;

        var customer = await TryReadCustomerByReferenceAsync(userReference, ct);
        if (customer?.Id is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId: customer.Id.Value, ct: ct);
            return subscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate("list customer subscriptions", ex.Error, ex);
        }
        catch (Exception ex) when (IsTransportFault(ex, cancellationToken))
        {
            throw new BillingProviderException("The billing provider could not be reached.", HttpStatusCode.BadGateway, innerException: ex);
        }
        catch (JsonException ex)
        {
            throw new BillingProviderException("The billing provider returned a response that could not be processed.", HttpStatusCode.BadGateway, innerException: ex);
        }
    }

    // ---- internals ------------------------------------------------------------------------------

    private async Task<SubscriptionPlanList> GetPlansCoreAsync(CancellationToken ct)
    {
        var familyId = await ResolveProductFamilyIdAsync(ct);
        var familyIdText = familyId.ToString(CultureInfo.InvariantCulture);

        var plans = new List<SubscriptionPlan>();
        var truncated = false;
        var page = 1;

        while (true)
        {
            IReadOnlyList<ProductResponse> products;
            try
            {
                products = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyIdText,
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
                if (ex.Error.TryGetString(out var notFound))
                {
                    throw new BillingProviderException(
                        $"Maxio product family '{_settings.ProductFamilyHandle}' was not found: {notFound}",
                        HttpStatusCode.BadGateway, innerException: ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw Translate("list products for product family", raw, ex);
                }
                throw new BillingProviderException("Plans could not be listed (unrecognised error shape).", HttpStatusCode.BadGateway, innerException: ex);
            }
            catch (Exception ex) when (IsTransportFault(ex, ct))
            {
                throw new BillingProviderException("The billing provider could not be reached.", HttpStatusCode.BadGateway, innerException: ex);
            }
            catch (JsonException ex)
            {
                throw new BillingProviderException("The billing provider returned a response that could not be processed.", HttpStatusCode.BadGateway, innerException: ex);
            }

            foreach (var product in products)
            {
                var mapped = MapPlan(product.Product);
                if (mapped is not null)
                {
                    plans.Add(mapped);
                }
            }

            if (products.Count < PlanPageSize)
            {
                break;
            }
            if (page >= MaxPlanPages)
            {
                truncated = true;
                _logger.LogWarning("Plan listing hit the page cap ({MaxPlanPages}); returning a partial list.", MaxPlanPages);
                break;
            }
            page++;
        }

        return new SubscriptionPlanList { Plans = plans, Truncated = truncated };
    }

    /// <summary>
    /// Resolves the configured product-family handle to its numeric id via <c>ListProductFamilies</c>
    /// (the products-by-family endpoint requires the numeric id). Cached for the process lifetime.
    /// </summary>
    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        if (_cachedFamilyId is int cached)
        {
            return cached;
        }

        await _familyIdLock.WaitAsync(ct);
        try
        {
            if (_cachedFamilyId is int cachedInner)
            {
                return cachedInner;
            }

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
                throw Translate("list product families", ex.Error, ex);
            }
            catch (Exception ex) when (IsTransportFault(ex, ct))
            {
                throw new BillingProviderException("The billing provider could not be reached.", HttpStatusCode.BadGateway, innerException: ex);
            }
            catch (JsonException ex)
            {
                throw new BillingProviderException("The billing provider returned a response that could not be processed.", HttpStatusCode.BadGateway, innerException: ex);
            }

            var match = families
                .Select(f => f.ProductFamily)
                .FirstOrDefault(f => f is not null && string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.Ordinal));

            if (match?.Id is null)
            {
                throw new BillingProviderException(
                    $"Maxio product family '{_settings.ProductFamilyHandle}' was not found on this site.",
                    HttpStatusCode.BadGateway);
            }

            _cachedFamilyId = match.Id.Value;
            _logger.LogInformation("Resolved Maxio product family '{Handle}' to id {FamilyId}.", _settings.ProductFamilyHandle, _cachedFamilyId);
            return _cachedFamilyId.Value;
        }
        finally
        {
            _familyIdLock.Release();
        }
    }

    /// <summary>
    /// Read-or-create the Maxio customer for the shopper. The customer <c>reference</c> is the durable,
    /// server-enforced unique claim: a duplicate create (concurrent double-click) is rejected by Maxio
    /// (422) and reconciled by re-reading.
    /// </summary>
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
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Reference = request.UserReference
            }
        };

        try
        {
            var created = await _client.Customers.CreateCustomer(body: body, ct: ct);
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", created.Customer.Id, request.UserReference);
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // 422 is most often the reference-uniqueness rejection under a concurrent double-click:
            // re-read by reference and use the existing customer.
            var reread = await TryReadCustomerByReferenceAsync(request.UserReference, ct);
            if (reread is not null)
            {
                return reread;
            }
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new BillingProviderException("The customer could not be created (validation error).", HttpStatusCode.UnprocessableEntity, isCallerError: true, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate("create customer", raw, ex);
            }
            throw new BillingProviderException("The customer could not be created (unrecognised error shape).", HttpStatusCode.BadGateway, innerException: ex);
        }
        catch (Exception ex) when (IsTransportFault(ex, ct))
        {
            // Unknown outcome: the create may have landed. Reconcile by reference.
            var reread = await TryReadCustomerByReferenceAsync(request.UserReference, ct);
            if (reread is not null)
            {
                return reread;
            }
            throw new BillingProviderException("The billing provider could not be reached.", HttpStatusCode.BadGateway, innerException: ex);
        }
        catch (JsonException ex)
        {
            throw new BillingProviderException("The billing provider returned a response that could not be processed.", HttpStatusCode.BadGateway, innerException: ex);
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate("read customer by reference", ex.Error, ex);
        }
        catch (Exception ex) when (IsTransportFault(ex, ct))
        {
            throw new BillingProviderException("The billing provider could not be reached.", HttpStatusCode.BadGateway, innerException: ex);
        }
        catch (JsonException ex)
        {
            throw new BillingProviderException("The billing provider returned a response that could not be processed.", HttpStatusCode.BadGateway, innerException: ex);
        }
    }

    private async Task<CustomerSubscription?> FindExistingLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate("list customer subscriptions", ex.Error, ex);
        }
        catch (Exception ex) when (IsTransportFault(ex, ct))
        {
            throw new BillingProviderException("The billing provider could not be reached.", HttpStatusCode.BadGateway, innerException: ex);
        }
        catch (JsonException ex)
        {
            throw new BillingProviderException("The billing provider returned a response that could not be processed.", HttpStatusCode.BadGateway, innerException: ex);
        }

        foreach (var response in subscriptions)
        {
            var subscription = response.Subscription;
            if (subscription is null)
            {
                continue;
            }
            var handle = subscription.Product?.Handle;
            var state = subscription.State?.Value;
            if (string.Equals(handle, planHandle, StringComparison.Ordinal) && state is not null && LiveStates.Contains(state))
            {
                return MapSubscription(subscription);
            }
        }
        return null;
    }

    private async Task<CustomerSubscription?> TryFindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference: reference, ct: ct);
            return response.Subscription is null ? null : MapSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            // Any other error during reconciliation: treat as "not found" so the caller learns the
            // outcome is unknown rather than a false success.
            _logger.LogWarning(ex, "Reconciliation lookup for subscription {Reference} failed.", reference);
            return null;
        }
        catch (Exception ex) when (IsTransportFault(ex, ct))
        {
            _logger.LogWarning(ex, "Reconciliation lookup for subscription {Reference} could not reach the provider.", reference);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Reconciliation lookup for subscription {Reference} returned an unprocessable response.", reference);
            return null;
        }
    }

    private static SubscriptionPlan? MapPlan(Product product)
    {
        // A plan with no handle cannot be subscribed to; skip it.
        if (string.IsNullOrWhiteSpace(product.Handle))
        {
            return null;
        }
        return new SubscriptionPlan
        {
            Handle = product.Handle!,
            Name = product.Name,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit?.Value,
            ProductId = product.Id
        };
    }

    private static CustomerSubscription MapSubscription(Subscription subscription)
    {
        return new CustomerSubscription
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            State = subscription.State?.Value ?? "unknown",
            PriceInCents = subscription.ProductPriceInCents,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    private static string BuildSubscriptionReference(string userReference, string planHandle)
        => $"eshop:{userReference}:{planHandle}";

    private static string DescribeErrors(string prefix, IReadOnlyList<string> errors)
    {
        var detail = errors is { Count: > 0 } ? string.Join("; ", errors) : "no detail provided";
        return $"{prefix}: {detail}";
    }

    /// <summary>
    /// Translate a Case-B <see cref="RawError"/> into a <see cref="BillingProviderException"/>, keying the
    /// caller-facing status on the provider status: our-fault statuses (401/403/429) become 502/503, a
    /// caller 4xx passes through, everything else becomes 502.
    /// </summary>
    private BillingProviderException Translate(string operation, RawError raw, Exception inner)
    {
        var status = raw.StatusCode;
        var providerBody = SafeReadBody(raw);
        _logger.LogError("Maxio {Operation} failed: HTTP {Status}. {Body}", operation, (int)status, providerBody);

        var code = (int)status;
        if (code is 401 or 403)
        {
            return new BillingProviderException("The billing provider rejected our credentials.", HttpStatusCode.BadGateway, innerException: inner);
        }
        if (code == 429)
        {
            return new BillingProviderException("The billing provider is rate-limiting requests.", HttpStatusCode.ServiceUnavailable, innerException: inner);
        }
        if (code is >= 400 and < 500)
        {
            return new BillingProviderException($"The billing provider rejected the request ({operation}).", status, isCallerError: true, inner);
        }
        return new BillingProviderException($"The billing provider failed to process the request ({operation}).", HttpStatusCode.BadGateway, innerException: inner);
    }

    private static string SafeReadBody(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch
        {
            return "<unreadable body>";
        }
    }

    /// <summary>
    /// A transport/connection fault (not an API error). A caller-initiated cancellation is NOT a transport
    /// fault and is allowed to propagate; the SDK's per-attempt timeout and our budget surface as a
    /// <see cref="TaskCanceledException"/> whose cancellation token is not the caller's.
    /// </summary>
    private static bool IsTransportFault(Exception ex, CancellationToken callerToken)
    {
        if (ex is HttpRequestException)
        {
            return true;
        }
        if (ex is TaskCanceledException or OperationCanceledException)
        {
            return !callerToken.IsCancellationRequested;
        }
        return false;
    }

    private static CancellationTokenSource CreateBudget(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return cts;
    }
}
