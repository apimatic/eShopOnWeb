using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing. Maxio is the system of record:
/// idempotency claims live in Maxio (customer/subscription <c>reference</c> uniqueness), not in a local store
/// (the app runs on an in-memory DB that survives neither restarts nor a second host). Every SDK failure is
/// translated to <see cref="SubscriptionBillingException"/> with a caller-safe message.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    private const int TotalBudgetSeconds = 30;
    private const int PlansPerPage = 200;
    private const int MaxPlanPages = 50;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    // ----- Plans -------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        using var budget = CreateBudget(cancellationToken);
        var plans = await GetPlansCoreAsync(budget.Token, cancellationToken);
        _logger.LogInformation("Maxio: served {Count} subscription plan(s) for family {Family}.", plans.Count, _settings.ProductFamilyHandle);
        return plans;
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> GetPlansCoreAsync(CancellationToken ct, CancellationToken callerCt)
    {
        // The family-products route takes the handle prefixed with "handle:" (a bare handle 404s).
        var familyId = "handle:" + _settings.ProductFamilyHandle;
        var plans = new List<SubscriptionPlan>();
        int page = 1;

        while (true)
        {
            IReadOnlyList<ProductResponse> products;
            try
            {
                products = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PlansPerPage,
                    ct: ct);
            }
            catch (OperationCanceledException) when (callerCt.IsCancellationRequested)
            {
                throw;
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                // 404 here means the configured family handle does not exist — a deployment/config fault,
                // not something the caller can fix.
                if (ex.Error.TryGetString(out _))
                {
                    throw new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderUnavailable,
                        "The configured subscription catalog is unavailable.", 404, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw MapRawError(raw, ex);
                }
                throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Unknown,
                    "An unexpected billing error occurred.", null, ex);
            }
            catch (Exception ex) when (ex is SdkException<RawError> or JsonException or HttpRequestException or AuthSchemeException or OperationCanceledException)
            {
                throw MapFailure(ex);
            }

            foreach (var pr in products)
            {
                plans.Add(MapPlan(pr.Product));
            }

            if (products.Count < PlansPerPage)
            {
                break; // last (short) page — full set retrieved
            }
            if (++page > MaxPlanPages)
            {
                // Provider-independent bound so a misbehaving pager can't loop forever. Surface it, don't hide it.
                _logger.LogWarning("Maxio: plan listing hit the {MaxPages}-page cap; returning a partial catalog.", MaxPlanPages);
                break;
            }
        }

        return plans;
    }

    // ----- Subscribe (idempotent) --------------------------------------------------------------------

    public async Task<SubscribeOutcome> SubscribeAsync(string userReference, string email, string? planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userReference))
        {
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.InvalidRequest, "A user identity is required to subscribe.");
        }

        using var budget = CreateBudget(cancellationToken);
        var ct = budget.Token;

        // Cross-operation invariant: the effective handle must be one the family actually offers.
        var plans = await GetPlansCoreAsync(ct, cancellationToken);

        // Resolve the effective plan: explicit request → configured default → first available (all catalog-agnostic).
        var effectiveHandle = planHandle;
        if (string.IsNullOrWhiteSpace(effectiveHandle))
        {
            effectiveHandle = _settings.DefaultPlanHandle;
        }
        if (string.IsNullOrWhiteSpace(effectiveHandle))
        {
            effectiveHandle = plans.FirstOrDefault()?.Handle;
        }
        if (string.IsNullOrWhiteSpace(effectiveHandle))
        {
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderUnavailable,
                "No subscription plans are currently available.");
        }
        if (!plans.Any(p => string.Equals(p.Handle, effectiveHandle, StringComparison.Ordinal)))
        {
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.InvalidRequest,
                $"Unknown plan '{effectiveHandle}'. Choose one of the available subscription plans.");
        }
        var resolvedHandle = effectiveHandle!;

        _logger.LogInformation("Maxio: subscribe requested for user {User} to plan {Plan}.", userReference, resolvedHandle);

        // Ensure a Maxio customer exists (idempotent by reference).
        var customerId = await EnsureCustomerAsync(userReference, email, ct, cancellationToken);

        // Idempotency key for the subscription: deterministic per (user, plan).
        var subscriptionReference = BuildSubscriptionReference(userReference, resolvedHandle);

        // Fast path: a subscription for this (user, plan) already exists → return it, no second create.
        var existing = await TryFindSubscriptionAsync(subscriptionReference, ct, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Maxio: existing subscription {Id} reused for user {User}/{Plan} (state {State}).",
                existing.Id, userReference, resolvedHandle, existing.State?.Value);
            return new SubscribeOutcome(MapSubscription(existing), AlreadyExisted: true, customerId);
        }

        // Card-free plans: use invoice/remittance collection so signup never attempts an immediate card charge.
        var collectionMethod = string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod)
            ? CollectionMethod.Remittance
            : CollectionMethod.FromValue(_settings.PaymentCollectionMethod);

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = resolvedHandle,
                        CustomerId = customerId,
                        Reference = subscriptionReference,
                        PaymentCollectionMethod = collectionMethod
                    }
                },
                ct: ct);

            var subscription = response.Subscription
                ?? throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Unknown,
                    "The billing provider accepted the subscription but returned no details.");

            _logger.LogInformation("Maxio: created subscription {Id} for user {User}/{Plan} (state {State}).",
                subscription.Id, userReference, resolvedHandle, subscription.State?.Value);
            return new SubscribeOutcome(MapSubscription(subscription), AlreadyExisted: false, customerId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // A concurrent create for the same reference (or a transient) may already have won the race:
            // reconcile by re-reading before treating this as a validation failure.
            var reconciled = await TryFindSubscriptionAsync(subscriptionReference, ct, cancellationToken);
            if (reconciled is not null)
            {
                _logger.LogInformation("Maxio: subscription {Id} reconciled after create conflict for user {User}/{Plan}.",
                    reconciled.Id, userReference, resolvedHandle);
                return new SubscribeOutcome(MapSubscription(reconciled), AlreadyExisted: true, customerId);
            }

            if (ex.Error.TryGetErrorListResponse1(out var body) && body.Errors.Count > 0)
            {
                _logger.LogWarning("Maxio: subscription rejected for user {User}/{Plan}: {Errors}.",
                    userReference, resolvedHandle, string.Join("; ", body.Errors));
                throw new SubscriptionBillingException(SubscriptionBillingErrorKind.InvalidRequest,
                    "The subscription was rejected: " + string.Join("; ", body.Errors), 422, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, ex);
            }
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Unknown,
                "An unexpected billing error occurred while subscribing.", null, ex);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or AuthSchemeException or OperationCanceledException)
        {
            // Transport failed after the POST may have been received: the write outcome is unknown, so re-read
            // by reference before reporting failure.
            var reconciled = await TryFindSubscriptionAsync(subscriptionReference, ct, cancellationToken);
            if (reconciled is not null)
            {
                return new SubscribeOutcome(MapSubscription(reconciled), AlreadyExisted: true, customerId);
            }
            throw MapFailure(ex);
        }
    }

    // ----- My subscriptions --------------------------------------------------------------------------

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userReference))
        {
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.InvalidRequest, "A user identity is required.");
        }

        using var budget = CreateBudget(cancellationToken);
        var ct = budget.Token;

        var customer = await TryReadCustomerByReferenceAsync(userReference, ct, cancellationToken);
        if (customer?.Id is not int customerId)
        {
            // No Maxio customer yet → the user simply has no subscriptions.
            return Array.Empty<CustomerSubscription>();
        }

        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is SdkException<RawError> or JsonException or HttpRequestException or AuthSchemeException or OperationCanceledException)
        {
            throw MapFailure(ex);
        }

        var result = subscriptions
            .Where(s => s.Subscription is not null)
            .Select(s => MapSubscription(s.Subscription!))
            .ToList();

        _logger.LogInformation("Maxio: returned {Count} subscription(s) for user {User}.", result.Count, userReference);
        return result;
    }

    // ----- Customer (idempotent by reference) --------------------------------------------------------

    private async Task<int> EnsureCustomerAsync(string userReference, string email, CancellationToken ct, CancellationToken callerCt)
    {
        var existing = await TryReadCustomerByReferenceAsync(userReference, ct, callerCt);
        if (existing?.Id is int existingId)
        {
            return existingId;
        }

        var (firstName, lastName) = DeriveName(email, userReference);
        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = userReference
                    }
                },
                ct: ct);

            if (response.Customer.Id is int newId)
            {
                _logger.LogInformation("Maxio: created customer {Id} for user {User}.", newId, userReference);
                return newId;
            }
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Unknown,
                "The billing provider created the customer but returned no id.");
        }
        catch (OperationCanceledException) when (callerCt.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // Reference uniqueness is enforced by Maxio: a concurrent create loses the race with a 422.
            // Reconcile by re-reading before reporting a validation failure.
            var reconciled = await TryReadCustomerByReferenceAsync(userReference, ct, callerCt);
            if (reconciled?.Id is int reconciledId)
            {
                return reconciledId;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                throw new SubscriptionBillingException(SubscriptionBillingErrorKind.InvalidRequest,
                    DescribeCustomerError(body), 422, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, ex);
            }
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Unknown,
                "An unexpected billing error occurred while creating the customer.", null, ex);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or AuthSchemeException or OperationCanceledException)
        {
            // Unknown write outcome → re-read by reference.
            var reconciled = await TryReadCustomerByReferenceAsync(userReference, ct, callerCt);
            if (reconciled?.Id is int reconciledId)
            {
                return reconciledId;
            }
            throw MapFailure(ex);
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct, CancellationToken callerCt)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null; // no customer with this reference yet
        }
        catch (OperationCanceledException) when (callerCt.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is SdkException<RawError> or JsonException or HttpRequestException or AuthSchemeException or OperationCanceledException)
        {
            throw MapFailure(ex);
        }
    }

    private async Task<Subscription?> TryFindSubscriptionAsync(string reference, CancellationToken ct, CancellationToken callerCt)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference: reference, ct: ct);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null; // 404 — no subscription with this reference
        }
        catch (OperationCanceledException) when (callerCt.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, ex);
            }
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Unknown,
                "An unexpected billing error occurred.", null, ex);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or AuthSchemeException or OperationCanceledException)
        {
            throw MapFailure(ex);
        }
    }

    // ----- Mapping & helpers -------------------------------------------------------------------------

    private static CancellationTokenSource CreateBudget(CancellationToken callerCt)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(callerCt);
        cts.CancelAfter(TimeSpan.FromSeconds(TotalBudgetSeconds));
        return cts;
    }

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        PriceFormatted = FormatMoney(product.PriceInCents ?? 0),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
        RequiresPaymentMethod = product.RequireCreditCard ?? false
    };

    private static CustomerSubscription MapSubscription(Subscription subscription)
    {
        var state = subscription.State?.Value ?? "unknown";
        // Only genuinely live states report as active; every problem/end-of-life state is surfaced verbatim.
        var isActive = subscription.State == SubscriptionState.Active || subscription.State == SubscriptionState.Trialing;

        return new CustomerSubscription
        {
            SubscriptionId = subscription.Id ?? 0,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            State = state,
            IsActive = isActive,
            PriceInCents = subscription.ProductPriceInCents,
            PriceFormatted = subscription.ProductPriceInCents is long cents ? FormatMoney(cents) : null,
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt,
            Reference = subscription.Reference
        };
    }

    private static string FormatMoney(long cents) =>
        "$" + (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static string BuildSubscriptionReference(string userReference, string planHandle) =>
        $"eshop:{userReference}:{planHandle}";

    private static (string FirstName, string LastName) DeriveName(string email, string userReference)
    {
        var source = !string.IsNullOrWhiteSpace(email) ? email : userReference;
        var atIndex = source.IndexOf('@');
        var local = atIndex > 0 ? source[..atIndex] : source;
        var firstName = string.IsNullOrWhiteSpace(local) ? "eShop" : local;
        return (firstName, "eShopOnWeb");
    }

    private static string DescribeCustomerError(CustomerErrorResponse1 body)
    {
        if (body.Errors is { } errors && errors.TryGetListOfString(out var messages) && messages.Count > 0)
        {
            return "The customer could not be created: " + string.Join("; ", messages);
        }
        return "The customer could not be created.";
    }

    private static SubscriptionBillingException MapRawError(RawError raw, Exception source)
    {
        var status = (int)raw.StatusCode;
        return status switch
        {
            // Our credentials / our quota — the caller did nothing wrong and cannot fix it.
            401 or 403 or 429 => new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderUnavailable,
                "The billing provider is temporarily unavailable.", status, source),
            404 => new SubscriptionBillingException(SubscriptionBillingErrorKind.NotFound,
                "The requested billing resource was not found.", status, source),
            409 => new SubscriptionBillingException(SubscriptionBillingErrorKind.Conflict,
                "The billing request conflicts with the current state.", status, source),
            >= 400 and < 500 => new SubscriptionBillingException(SubscriptionBillingErrorKind.InvalidRequest,
                "The billing provider rejected the request.", status, source),
            _ => new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderUnavailable,
                "The billing provider is temporarily unavailable.", status, source)
        };
    }

    private static SubscriptionBillingException MapFailure(Exception ex) => ex switch
    {
        SdkException<RawError> raw => MapRawError(raw.Error, raw),
        AuthSchemeException => new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderUnavailable,
            "The billing provider rejected our credentials.", null, ex),
        JsonException => new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderUnavailable,
            "The billing provider returned a response that could not be processed.", null, ex),
        OperationCanceledException => new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderUnavailable,
            "The billing provider did not respond in time.", null, ex),
        HttpRequestException => new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderUnavailable,
            "The billing provider is unreachable.", null, ex),
        _ => new SubscriptionBillingException(SubscriptionBillingErrorKind.Unknown,
            "An unexpected billing error occurred.", null, ex)
    };
}
