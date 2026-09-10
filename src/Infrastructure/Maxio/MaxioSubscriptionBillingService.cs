using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
///
/// Idempotency: a Maxio customer is keyed by a deterministic <c>reference</c> derived from the
/// eShopOnWeb user identity, and a subscription by a deterministic reference derived from
/// user+plan. Each write is preceded by a read of that reference, and enrollment is serialized
/// per user with an in-process lock — so a double-click never creates a second customer or
/// subscription. All provider failures are translated into <see cref="SubscriptionBillingException"/>.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    // Per-call budget: the SDK's Timeout is per-attempt, so the only bound on a whole call is a
    // CancellationToken deadline. Enforced in Bounded(...).
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // Serializes enrollment per user to close the check-then-create race within a process.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    // The configured product family's numeric id is resolved once from its handle and cached.
    private readonly SemaphoreSlim _familyIdGate = new(1, 1);
    private int? _cachedFamilyId;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await GetFamilyProductsAsync(cancellationToken);
        return products
            .Where(p => p.Product is not null)
            .Select(p => ToPlan(p.Product!))
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriptionEnrollmentRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserIdentity))
        {
            throw new SubscriptionBillingException("A user identity is required to subscribe.", SubscriptionBillingErrorKind.InvalidRequest);
        }

        // Resolve and validate the target plan against the configured family (server-side truth),
        // so a caller cannot subscribe to a product outside the family or to a nonexistent handle.
        var plans = await GetPlansAsync(cancellationToken);
        if (plans.Count == 0)
        {
            throw new SubscriptionBillingException("No subscription plans are available.", SubscriptionBillingErrorKind.ProviderUnavailable);
        }

        SubscriptionPlan plan;
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            plan = plans[0];
        }
        else
        {
            var match = plans.FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new SubscriptionBillingException(
                    $"Plan '{request.PlanHandle}' is not available in this catalog.", SubscriptionBillingErrorKind.InvalidRequest);
            }
            plan = match;
        }

        var customerReference = BuildCustomerReference(request.UserIdentity);

        var gate = UserLocks.GetOrAdd(request.UserIdentity, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var (customerId, customerAlreadyExisted) = await EnsureCustomerAsync(request, customerReference, cancellationToken);

            // Idempotent enrollment: if the customer already has a live subscription to this plan,
            // return it instead of creating a second one. The per-user lock above serializes
            // concurrent double-clicks so this check-then-create cannot race within a process, and
            // because it queries Maxio (the system of record) it also holds across process restarts.
            var existing = await FindLiveSubscriptionForPlanAsync(customerId, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Maxio enrollment idempotent hit: customer {CustomerId} already has live subscription {SubscriptionId} for plan {PlanHandle}.",
                    customerId, existing.Id, plan.Handle);
                return new SubscribeResult
                {
                    Subscription = ToSubscriptionInfo(existing),
                    CustomerId = customerId,
                    AlreadyExisted = true,
                    CustomerAlreadyExisted = customerAlreadyExisted
                };
            }

            var created = await CreateSubscriptionAsync(customerId, plan.Handle, BuildSubscriptionReference(request.UserIdentity, plan.Handle), cancellationToken);
            _logger.LogInformation(
                "Maxio enrollment created subscription {SubscriptionId} (state {State}) for customer {CustomerId}, plan {PlanHandle}.",
                created.Id, created.State?.Value, customerId, plan.Handle);

            return new SubscribeResult
            {
                Subscription = ToSubscriptionInfo(created),
                CustomerId = customerId,
                AlreadyExisted = false,
                CustomerAlreadyExisted = customerAlreadyExisted
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscriptionInfo>> GetSubscriptionsForUserAsync(string userIdentity, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userIdentity))
        {
            throw new SubscriptionBillingException("A user identity is required.", SubscriptionBillingErrorKind.InvalidRequest);
        }

        var reference = BuildCustomerReference(userIdentity);
        var customer = await ReadCustomerByReferenceAsync(reference, cancellationToken);
        if (customer?.Id is null)
        {
            // No billing customer yet — the user simply has no subscriptions.
            return Array.Empty<CustomerSubscriptionInfo>();
        }

        var subscriptions = await Bounded(
            ct => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: ct),
            "ListCustomerSubscriptions", cancellationToken);

        return subscriptions
            .Where(s => s.Subscription is not null)
            .Select(s => ToSubscriptionInfo(s.Subscription!))
            .ToList();
    }

    // ----- Maxio calls -----

    private async Task<IReadOnlyList<ProductResponse>> GetFamilyProductsAsync(CancellationToken cancellationToken)
    {
        var familyId = await ResolveFamilyIdAsync(cancellationToken);
        return await Bounded(
            ct => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(),
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
                ct: ct),
            "ListProductsForProductFamily", cancellationToken);
    }

    private async Task<int> ResolveFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_cachedFamilyId is int cached)
        {
            return cached;
        }

        await _familyIdGate.WaitAsync(cancellationToken);
        try
        {
            if (_cachedFamilyId is int already)
            {
                return already;
            }

            var families = await Bounded(
                ct => _client.ProductFamilies.ListProductFamilies(
                    dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct),
                "ListProductFamilies", cancellationToken);

            var handle = _options.ProductFamilyHandle;
            var match = families.FirstOrDefault(f =>
                string.Equals(f.ProductFamily?.Handle, handle, StringComparison.OrdinalIgnoreCase));

            if (match?.ProductFamily?.Id is not int id)
            {
                throw new SubscriptionBillingException(
                    $"The configured product family '{handle}' was not found in the billing system.",
                    SubscriptionBillingErrorKind.ProviderUnavailable);
            }

            _cachedFamilyId = id;
            return id;
        }
        finally
        {
            _familyIdGate.Release();
        }
    }

    private async Task<(int CustomerId, bool AlreadyExisted)> EnsureCustomerAsync(
        SubscriptionEnrollmentRequest request, string reference, CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerByReferenceAsync(reference, cancellationToken);
        if (existing?.Id is int existingId)
        {
            return (existingId, true);
        }

        var email = !string.IsNullOrWhiteSpace(request.Email) ? request.Email! : request.UserIdentity;
        var (firstName, lastName) = DeriveName(email);

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        try
        {
            var response = await Bounded(ct => _client.Customers.CreateCustomer(body, ct: ct), "CreateCustomer", cancellationToken);
            if (response.Customer?.Id is not int newId)
            {
                throw new SubscriptionBillingException(
                    "The billing system did not return a customer id.", SubscriptionBillingErrorKind.ProviderUnavailable);
            }
            _logger.LogInformation("Maxio customer {CustomerId} created for reference {Reference}.", newId, reference);
            return (newId, false);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw TranslateCreateCustomer(ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport("CreateCustomer", ex);
        }
        catch (JsonException ex)
        {
            throw MalformedBody("CreateCustomer", ex);
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct),
                "ReadCustomerByReference", cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // Expected control flow: the customer does not exist yet.
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw("ReadCustomerByReference", ex.Error, ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport("ReadCustomerByReference", ex);
        }
        catch (JsonException ex)
        {
            throw MalformedBody("ReadCustomerByReference", ex);
        }
    }

    private async Task<Subscription?> FindLiveSubscriptionForPlanAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await Bounded(
                ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                "ListCustomerSubscriptions", cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw("ListCustomerSubscriptions", ex.Error, ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport("ListCustomerSubscriptions", ex);
        }
        catch (JsonException ex)
        {
            throw MalformedBody("ListCustomerSubscriptions", ex);
        }

        // A live (non-terminal) subscription to the same plan is the idempotency anchor. Reference
        // uniqueness is enforced by Maxio, so a canceled subscription's reference cannot be reused —
        // which is exactly why we key on live state + plan rather than on the reference.
        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null
                && string.Equals(s!.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
                && IsLiveState(s.State?.Value))
            .OrderByDescending(s => s!.Id)
            .FirstOrDefault();
    }

    // States in which the old subscription is truly gone, so "subscribe again" should create a new
    // one. on_hold/suspended are resumable and therefore treated as live (not duplicated).
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private static bool IsLiveState(string? state)
        => !string.IsNullOrEmpty(state) && !TerminalStates.Contains(state);

    private async Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                Reference = reference,
                // Bill by invoice/remittance rather than automatic card collection, so enrollment
                // succeeds without a stored payment method (these plans do not require one).
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        try
        {
            var response = await Bounded(ct => _client.Subscriptions.CreateSubscription(body, ct: ct), "CreateSubscription", cancellationToken);
            if (response.Subscription is null)
            {
                throw new SubscriptionBillingException(
                    "The billing system did not return the created subscription.", SubscriptionBillingErrorKind.ProviderUnavailable);
            }
            return response.Subscription;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw TranslateCreateSubscription(ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport("CreateSubscription", ex);
        }
        catch (JsonException ex)
        {
            throw MalformedBody("CreateSubscription", ex);
        }
    }

    // ----- Error translation (one place; a single caller-facing failure type) -----

    private SubscriptionBillingException TranslateCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            LogProviderFailure("CreateCustomer", HttpStatusCode.UnprocessableEntity, null);
            return new SubscriptionBillingException(
                "The billing system rejected the customer details.", SubscriptionBillingErrorKind.InvalidRequest, ex);
        }
        HttpStatusCode? status = ex.Error.TryGetRawError(out var raw) ? raw.StatusCode : null;
        return Classified("CreateCustomer", status, ex, raw);
    }

    private SubscriptionBillingException TranslateCreateSubscription(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var errors))
        {
            var detail = errors.Errors is { Count: > 0 } list ? string.Join("; ", list) : "validation failed";
            LogProviderFailure("CreateSubscription", HttpStatusCode.UnprocessableEntity, detail);
            return new SubscriptionBillingException(
                $"The subscription could not be created: {detail}.", SubscriptionBillingErrorKind.InvalidRequest, ex);
        }
        HttpStatusCode? status = ex.Error.TryGetRawError(out var raw) ? raw.StatusCode : null;
        return Classified("CreateSubscription", status, ex, raw);
    }

    private SubscriptionBillingException TranslateRaw(string operation, RawError error, Exception ex)
        => Classified(operation, error.StatusCode, ex, error);

    private SubscriptionBillingException Classified(string operation, HttpStatusCode? status, Exception ex, RawError? raw = null)
    {
        var body = SafeReadBody(raw);
        LogProviderFailure(operation, status, body);

        var kind = ClassifyStatus(status);
        var message = kind switch
        {
            SubscriptionBillingErrorKind.InvalidRequest => "The billing system rejected the request.",
            SubscriptionBillingErrorKind.NotFound => "The requested billing resource was not found.",
            _ => "The billing provider is currently unavailable. Please try again later."
        };
        return new SubscriptionBillingException(message, kind, ex);
    }

    private SubscriptionBillingException Transport(string operation, Exception ex)
    {
        // No response arrived. On a write (POST) the SDK does not resend and the outcome is unknown;
        // the deterministic references make the next attempt reconcile rather than duplicate.
        _logger.LogWarning(ex, "Maxio {Operation} failed to reach the billing provider.", operation);
        return new SubscriptionBillingException(
            "The billing provider is currently unreachable. Please try again later.",
            SubscriptionBillingErrorKind.ProviderUnavailable, ex);
    }

    private SubscriptionBillingException MalformedBody(string operation, JsonException ex)
    {
        _logger.LogError(ex, "Maxio {Operation} returned a response that could not be processed.", operation);
        return new SubscriptionBillingException(
            "The billing provider returned a response that could not be processed.",
            SubscriptionBillingErrorKind.ProviderUnavailable, ex);
    }

    private void LogProviderFailure(string operation, HttpStatusCode? status, string? body)
        => _logger.LogWarning(
            "Maxio {Operation} failed with status {Status}. {Body}",
            operation, status is null ? "n/a" : ((int)status).ToString(), body ?? string.Empty);

    private static SubscriptionBillingErrorKind ClassifyStatus(HttpStatusCode? status) => status switch
    {
        null => SubscriptionBillingErrorKind.ProviderUnavailable,
        HttpStatusCode.NotFound => SubscriptionBillingErrorKind.NotFound,
        // Our credentials / our quota — the caller did nothing wrong and cannot fix it.
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
            => SubscriptionBillingErrorKind.ProviderUnavailable,
        >= (HttpStatusCode)400 and < (HttpStatusCode)500 => SubscriptionBillingErrorKind.InvalidRequest,
        _ => SubscriptionBillingErrorKind.ProviderUnavailable
    };

    private static bool IsTransport(Exception ex)
        => ex is System.Net.Http.HttpRequestException or TaskCanceledException or OperationCanceledException;

    private static string? SafeReadBody(RawError? raw)
    {
        if (raw is null)
        {
            return null;
        }
        try
        {
            var text = raw.ReadAsString();
            return string.IsNullOrWhiteSpace(text) ? null : Truncate(text, 500);
        }
        catch
        {
            return null;
        }
    }

    // ----- Mapping to application-core DTOs -----

    private static SubscriptionPlan ToPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? "Plan",
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        IntervalCount = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value
    };

    private static CustomerSubscriptionInfo ToSubscriptionInfo(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        State = subscription.State?.Value,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt
    };

    // ----- Deterministic references / helpers -----

    // The customer reference is deterministic per user, so ensure-customer is idempotent across runs.
    internal static string BuildCustomerReference(string userIdentity)
        => $"eshop-user-{Slug(userIdentity)}";

    // The subscription reference is traceable but unique: Maxio enforces reference uniqueness, and a
    // canceled subscription keeps its reference forever, so re-subscribing must not reuse it.
    // Idempotency is provided by the live-subscription check, not by this reference.
    internal static string BuildSubscriptionReference(string userIdentity, string planHandle)
        => $"eshop-sub-{Slug(userIdentity)}-{Slug(planHandle)}-{Guid.NewGuid():N}";

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var first = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;
        return (first, "eShopOnWeb");
    }

    /// <summary>Deterministic, reference-safe slug: lowercase, non-alphanumerics collapsed to hyphens.</summary>
    private static string Slug(string value)
    {
        var sb = new StringBuilder(value.Length);
        var lastHyphen = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastHyphen = false;
            }
            else if (!lastHyphen)
            {
                sb.Append('-');
                lastHyphen = true;
            }
        }
        return sb.ToString().Trim('-');
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    // ----- Whole-call budget -----

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, string operation, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Maxio {Operation} exceeded the {Budget}s call budget.", operation, CallBudget.TotalSeconds);
            throw new SubscriptionBillingException(
                "The billing provider did not respond in time. Please try again later.",
                SubscriptionBillingErrorKind.ProviderUnavailable);
        }
    }
}
