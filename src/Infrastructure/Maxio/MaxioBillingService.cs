using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Maxio;
using Maxio.Core.ErrorResponse;
using Maxio.Core.Exceptions;
using Maxio.Errors;
using Maxio.Models;
using Maxio.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio-backed implementation of <see cref="ISubscriptionBillingService"/>. Every SDK call is wrapped so
/// provider errors, malformed bodies, and transport faults are translated into a single
/// <see cref="MaxioBillingException"/> with a caller-safe message; provider detail is logged here, never
/// returned to the caller. Each public operation is bounded by a total timeout budget (a linked
/// <see cref="CancellationTokenSource"/>), because the SDK's own timeout is per attempt.
/// </summary>
public sealed class MaxioBillingService : ISubscriptionBillingService
{
    private readonly MaxioClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    // Serializes a single user's concurrent subscribe calls in-process so a double-click cannot race two
    // customer/subscription creates. Combined with read-before-create against a deterministic reference,
    // this holds "at most one customer + one subscription per (user, plan)" within a process. See maxio-plan.md §5.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new(StringComparer.OrdinalIgnoreCase);

    // Handle -> numeric id resolution for the configured product family. The list-products endpoint takes
    // the family's numeric id, but handles are the stable identifier, so we resolve it once and cache it for
    // the process (ids only change across re-seeds, i.e. across runs — never within a run).
    private int? _productFamilyId;

    public MaxioBillingService(MaxioClient client, IOptions<MaxioSettings> settings, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = CreateBudget(cancellationToken);
        var ct = budget.Token;

        var familyId = await ResolveProductFamilyIdAsync(ct, cancellationToken);

        IReadOnlyList<ProductResponse> products;
        try
        {
            products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
                ct: ct);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex) when (ex.Error.TryGetString(out _))
        {
            // 404 — the configured product family handle does not exist on this site.
            _logger.LogError(ex, "Maxio product family {Handle} not found while listing plans.", _settings.ProductFamilyHandle);
            throw new MaxioBillingException("Subscription plans are not available.", MaxioBillingFailureKind.NotFound, ex);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw TranslateTypedFallback(ex, ex.Error, "list plans");
        }
        catch (Exception ex)
        {
            throw TranslateTransport(ex, "list plans", cancellationToken);
        }

        var plans = products
            .Select(p => p.Product)
            .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();

        _logger.LogInformation("Listed {Count} subscription plan(s) from product family {Handle}.", plans.Count, _settings.ProductFamilyHandle);
        return plans;
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle, CancellationToken cancellationToken = default)
    {
        if (subscriber is null) throw new ArgumentNullException(nameof(subscriber));

        // Resolve the plan first (also validates the requested handle) — outside the per-user lock, it is a read.
        var resolvedPlanHandle = await ResolvePlanHandleAsync(planHandle, cancellationToken);

        var gate = _userLocks.GetOrAdd(subscriber.UserName, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var budget = CreateBudget(cancellationToken);
            var ct = budget.Token;

            var customer = await EnsureCustomerAsync(subscriber, ct, cancellationToken);
            var subscriptionReference = BuildSubscriptionReference(subscriber.UserName, resolvedPlanHandle);

            // Idempotency: return an existing subscription with this deterministic reference instead of creating a second.
            var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, ct, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Subscribe is idempotent hit for user {User} plan {Plan}: existing subscription {SubId} ({State}).",
                    subscriber.UserName, resolvedPlanHandle, existing.Id, existing.State?.Value);
                return MapSubscription(existing, alreadyExisted: true);
            }

            var created = await CreateSubscriptionAsync(subscriber.UserName, resolvedPlanHandle, subscriptionReference, ct, cancellationToken);
            _logger.LogInformation(
                "Created subscription {SubId} ({State}) for user {User} on plan {Plan}, next billing {NextBilling}.",
                created.Id, created.State?.Value, subscriber.UserName, resolvedPlanHandle, created.CurrentPeriodEndsAt);
            return MapSubscription(created, alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        if (subscriber is null) throw new ArgumentNullException(nameof(subscriber));

        using var budget = CreateBudget(cancellationToken);
        var ct = budget.Token;

        var customer = await ReadCustomerByReferenceAsync(subscriber.UserName, ct, cancellationToken);
        if (customer?.Id is null)
        {
            // No Maxio customer yet => the user has never subscribed. Not an error — an empty list.
            return Array.Empty<CustomerSubscription>();
        }

        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, "list customer subscriptions", ex);
        }
        catch (Exception ex)
        {
            throw TranslateTransport(ex, "list customer subscriptions", cancellationToken);
        }

        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!, alreadyExisted: true))
            .ToList();
    }

    // ---- Maxio call sites (each with its own grounded error boundary) --------------------------------

    private async Task<Customer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken ct, CancellationToken callerToken)
    {
        var existing = await ReadCustomerByReferenceAsync(subscriber.UserName, ct, callerToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveNames(subscriber);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email,
                Reference = subscriber.UserName
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body, ct: ct);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {User}.", response.Customer.Id, subscriber.UserName);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex) when (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            _logger.LogWarning(ex, "Maxio rejected customer creation for user {User}.", subscriber.UserName);
            throw new MaxioBillingException("The billing provider rejected the customer details.", MaxioBillingFailureKind.InvalidRequest, ex);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw TranslateTypedFallback(ex, ex.Error, "create customer");
        }
        catch (Exception ex)
        {
            throw TranslateTransport(ex, "create customer", callerToken);
        }
    }

    /// <summary>Reads a customer by reference; returns null on a genuine 404 (absent), never on a parse/transport fault.</summary>
    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken ct, CancellationToken callerToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // absent — the only case that may fall through to a create
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, "read customer by reference", ex);
        }
        catch (Exception ex)
        {
            throw TranslateTransport(ex, "read customer by reference", callerToken);
        }
    }

    /// <summary>Finds a subscription by its deterministic reference; returns null on a 404 (none), never on a fault.</summary>
    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct, CancellationToken callerToken)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference, ct: ct);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null; // 404 — no subscription with this reference yet
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            throw TranslateTypedFallback(ex, ex.Error, "find subscription");
        }
        catch (Exception ex)
        {
            throw TranslateTransport(ex, "find subscription", callerToken);
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(string userName, string planHandle, string reference, CancellationToken ct, CancellationToken callerToken)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerReference = userName,
                Reference = reference,
                // Collect by invoice/remittance so no card charge is attempted at signup (the plans in scope
                // do not require a payment method). Configurable via Maxio:PaymentCollectionMethod.
                PaymentCollectionMethod = CollectionMethod.FromValue(_settings.PaymentCollectionMethod)
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct: ct);
            var subscription = response.Subscription
                ?? throw new MaxioBillingException("The billing provider returned an empty subscription.", MaxioBillingFailureKind.Unexpected);
            return subscription;
        }
        catch (SdkException<CreateSubscriptionError> ex) when (ex.Error.TryGetErrorListResponse1(out var errors))
        {
            // 422 — validation messages the caller can act on; safe to surface (no secrets in these strings).
            var joined = errors.Errors is { Count: > 0 } ? string.Join("; ", errors.Errors) : "The subscription request was rejected.";
            _logger.LogWarning(ex, "Maxio rejected subscription for user {User} on plan {Plan}: {Errors}", userName, planHandle, joined);
            throw new MaxioBillingException(joined, MaxioBillingFailureKind.InvalidRequest, ex);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw TranslateTypedFallback(ex, ex.Error, "create subscription");
        }
        catch (Exception ex)
        {
            throw TranslateTransport(ex, "create subscription", callerToken);
        }
    }

    private async Task<string> ResolvePlanHandleAsync(string? requestedHandle, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedHandle))
        {
            return requestedHandle.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_settings.DefaultPlanHandle))
        {
            return _settings.DefaultPlanHandle.Trim();
        }

        // No plan specified and no configured default: fall back to the first plan in the family (catalog-agnostic).
        var plans = await GetAvailablePlansAsync(cancellationToken);
        var first = plans.FirstOrDefault();
        if (first is null)
        {
            throw new MaxioBillingException("No subscription plans are available to subscribe to.", MaxioBillingFailureKind.NotFound);
        }
        return first.Handle;
    }

    /// <summary>Resolves the configured product family handle to its numeric id (cached for the process).</summary>
    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct, CancellationToken callerToken)
    {
        if (_productFamilyId is { } cached)
        {
            return cached;
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
            throw TranslateRawError(ex.Error, "list product families", ex);
        }
        catch (Exception ex)
        {
            throw TranslateTransport(ex, "list product families", callerToken);
        }

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f?.Id is not null
                && string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is null)
        {
            _logger.LogError("Configured Maxio product family handle {Handle} was not found on the site.", _settings.ProductFamilyHandle);
            throw new MaxioBillingException("Subscription plans are not available.", MaxioBillingFailureKind.NotFound);
        }

        _productFamilyId = match.Id.Value;
        return _productFamilyId.Value;
    }

    // ---- Mapping -------------------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(Product p) => new()
    {
        ProductId = p.Id ?? 0,
        Handle = p.Handle ?? string.Empty,
        Name = p.Name ?? string.Empty,
        Description = p.Description,
        PriceInCents = p.PriceInCents ?? 0,
        Interval = p.Interval ?? 0,
        IntervalUnit = p.IntervalUnit?.Value,
        RequiresPaymentMethod = p.RequireCreditCard ?? false
    };

    private static CustomerSubscription MapSubscription(Subscription s, bool alreadyExisted) => new()
    {
        SubscriptionId = s.Id ?? 0,
        Reference = s.Reference,
        PlanHandle = s.Product?.Handle,
        PlanName = s.Product?.Name,
        State = s.State?.Value,
        PriceInCents = s.ProductPriceInCents,
        Currency = s.Currency,
        CurrentPeriodStartedAt = s.CurrentPeriodStartedAt,
        NextBillingDate = s.CurrentPeriodEndsAt,
        CreatedAt = s.CreatedAt,
        AlreadyExisted = alreadyExisted
    };

    // ---- Helpers -------------------------------------------------------------------------------------

    private static string BuildSubscriptionReference(string userName, string planHandle) => $"eshop:{userName}:{planHandle}";

    private static (string FirstName, string LastName) DeriveNames(SubscriberIdentity subscriber)
    {
        var first = subscriber.FirstName;
        var last = subscriber.LastName;

        if (string.IsNullOrWhiteSpace(first))
        {
            var local = subscriber.Email;
            var at = local.IndexOf('@');
            first = at > 0 ? local.Substring(0, at) : local;
        }
        if (string.IsNullOrWhiteSpace(first)) first = "eShop";
        if (string.IsNullOrWhiteSpace(last)) last = "Subscriber";
        return (first!, last!);
    }

    private CancellationTokenSource CreateBudget(CancellationToken callerToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
        return cts;
    }

    /// <summary>Maps a Case-B <see cref="RawError"/> to a caller-safe failure, keyed on the transport status.</summary>
    private MaxioBillingException TranslateRawError(RawError raw, string operation, Exception source)
    {
        var status = (int)raw.StatusCode;
        _logger.LogError(source, "Maxio {Operation} failed with HTTP {Status}.", operation, status);

        return status switch
        {
            401 or 403 or 429 => new MaxioBillingException("The billing provider is currently unavailable. Please try again later.", MaxioBillingFailureKind.ProviderUnavailable, source),
            404 => new MaxioBillingException("The requested billing resource was not found.", MaxioBillingFailureKind.NotFound, source),
            409 => new MaxioBillingException("The request conflicts with the current subscription state.", MaxioBillingFailureKind.Conflict, source),
            >= 400 and < 500 => new MaxioBillingException("The billing provider rejected the request.", MaxioBillingFailureKind.InvalidRequest, source),
            >= 500 => new MaxioBillingException("The billing provider is currently unavailable. Please try again later.", MaxioBillingFailureKind.ProviderUnavailable, source),
            _ => new MaxioBillingException("An unexpected billing error occurred.", MaxioBillingFailureKind.Unexpected, source)
        };
    }

    /// <summary>The non-typed fallback arm of a Case-A catch ladder: read the status off <c>TryGetRawError</c>.</summary>
    private MaxioBillingException TranslateTypedFallback(Exception source, ApiError error, string operation)
    {
        if (error.TryGetRawError(out var raw))
        {
            return TranslateRawError(raw, operation, source);
        }
        _logger.LogError(source, "Maxio {Operation} failed with an unrecognized error shape.", operation);
        return new MaxioBillingException("An unexpected billing error occurred.", MaxioBillingFailureKind.Unexpected, source);
    }

    /// <summary>
    /// Translates transport faults and malformed bodies. A <see cref="JsonException"/> reaches here from
    /// either a drifted 2xx body or an error body that did not match its generated error shape — both are
    /// caller-safe "unavailable", and neither is ever treated as a domain absence (that decision is made
    /// only on an explicit 404 at the read sites above).
    /// </summary>
    private MaxioBillingException TranslateTransport(Exception ex, string operation, CancellationToken callerToken)
    {
        switch (ex)
        {
            case MaxioBillingException billing:
                return billing; // already translated (e.g. rethrown from a nested call site)
            case OperationCanceledException when callerToken.IsCancellationRequested:
                _logger.LogInformation("Maxio {Operation} canceled by caller.", operation);
                return new MaxioBillingException("The request was canceled.", MaxioBillingFailureKind.ProviderUnavailable, ex);
            case OperationCanceledException:
                _logger.LogError(ex, "Maxio {Operation} timed out.", operation);
                return new MaxioBillingException("The billing provider timed out. Please try again later.", MaxioBillingFailureKind.ProviderUnavailable, ex);
            case HttpRequestException:
                _logger.LogError(ex, "Maxio {Operation} could not reach the provider.", operation);
                return new MaxioBillingException("The billing provider is currently unreachable. Please try again later.", MaxioBillingFailureKind.ProviderUnavailable, ex);
            case JsonException:
                _logger.LogError(ex, "Maxio {Operation} returned a response that could not be processed.", operation);
                return new MaxioBillingException("The billing provider returned a response that could not be processed.", MaxioBillingFailureKind.ProviderUnavailable, ex);
            default:
                _logger.LogError(ex, "Maxio {Operation} failed unexpectedly.", operation);
                return new MaxioBillingException("An unexpected billing error occurred.", MaxioBillingFailureKind.Unexpected, ex);
        }
    }
}
