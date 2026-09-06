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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using CustomerSubscription = Microsoft.eShopWeb.ApplicationCore.Subscriptions.CustomerSubscription;
using SubscribeRequest = Microsoft.eShopWeb.ApplicationCore.Subscriptions.SubscribeRequest;
using SubscribeResult = Microsoft.eShopWeb.ApplicationCore.Subscriptions.SubscribeResult;
using SubscriberIdentity = Microsoft.eShopWeb.ApplicationCore.Subscriptions.SubscriberIdentity;
using SubscriptionPlan = Microsoft.eShopWeb.ApplicationCore.Subscriptions.SubscriptionPlan;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing. Maxio is the system of
/// record: nothing about a subscription is mirrored in the eShopOnWeb database, so the answer a
/// shopper sees is always the provider's own.
/// <para>
/// This class is the integration boundary. Every Maxio exception - API error, transport failure
/// or unreadable body - is converted here into a <see cref="BillingException"/> with a
/// caller-safe message, so no SDK type and no provider stack detail escapes into the API layer.
/// </para>
/// </summary>
public sealed class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>
    /// States in which a subscription has finally ended. Anything else - including a state this
    /// build does not recognise - counts as live, so an unrecognised state blocks a duplicate
    /// enrollment rather than causing one.
    /// </summary>
    private static readonly HashSet<string> EndedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        SubscriptionState.Canceled.Value,
        SubscriptionState.Expired.Value,
        SubscriptionState.FailedToCreate.Value,
        SubscriptionState.TrialEnded.Value
    };

    private const int PlanPageSize = 200;
    private const int MaxPlanPages = 25;
    private const int MaxReferenceAttempts = 50;
    private const int MaxProviderMessageLength = 500;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _subscriberLocks;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        MaxioSettings settings,
        IMemoryCache cache,
        KeyedAsyncLock subscriberLocks,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _cache = cache;
        _subscriberLocks = subscriberLocks;
        _logger = logger;
    }

    private string ProductFamilyHandle =>
        _settings.ProductFamilyHandle ?? throw new BillingConfigurationException(
            MaxioSettings.SectionName + ":ProductFamilyHandle is not configured.");

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = "maxio:plans:" + ProductFamilyHandle;

        if (_settings.PlanCacheDuration > TimeSpan.Zero &&
            _cache.TryGetValue(cacheKey, out IReadOnlyCollection<SubscriptionPlan>? cached) &&
            cached is not null)
        {
            return cached;
        }

        var plans = await FetchPlansAsync(cancellationToken).ConfigureAwait(false);

        if (_settings.PlanCacheDuration > TimeSpan.Zero)
        {
            _cache.Set(cacheKey, plans, _settings.PlanCacheDuration);
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = await ResolvePlanAsync(request.PlanHandle, cancellationToken).ConfigureAwait(false);
        var customerReference = MaxioReference.ForCustomer(request.Subscriber.UserName);

        // Serialise concurrent subscribe attempts for the same shopper so a double-click cannot
        // have both requests pass the "already subscribed?" check at the same time.
        using var subscriberLock = await _subscriberLocks.AcquireAsync(customerReference, cancellationToken).ConfigureAwait(false);

        var customerId = await EnsureCustomerAsync(request.Subscriber, customerReference, cancellationToken).ConfigureAwait(false);
        var existing = await ListSubscriptionsForCustomerAsync(customerId, cancellationToken).ConfigureAwait(false);

        var live = existing.FirstOrDefault(subscription =>
            subscription.IsLive &&
            string.Equals(subscription.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase));

        if (live is not null)
        {
            _logger.LogInformation(
                "Shopper {CustomerReference} already holds subscription {SubscriptionId} ({State}) for plan {PlanHandle}; not creating another.",
                customerReference, live.Id, live.State, plan.Handle);
            return new SubscribeResult(live, plan, alreadySubscribed: true);
        }

        var subscriptionReference = NextSubscriptionReference(customerReference, plan.Handle, existing);
        var created = await CreateSubscriptionAsync(customerId, plan, subscriptionReference, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Created Maxio subscription {SubscriptionId} ({Reference}) on plan {PlanHandle} for customer {CustomerId}.",
            created.Id, created.Reference, plan.Handle, customerId);

        return new SubscribeResult(created, plan, alreadySubscribed: false);
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var customerReference = MaxioReference.ForCustomer(subscriber.UserName);
        var customer = await FindCustomerAsync(customerReference, cancellationToken).ConfigureAwait(false);

        if (customer?.Id is not int customerId)
        {
            // Never subscribed: an empty list, not an error.
            return Array.Empty<CustomerSubscription>();
        }

        return await ListSubscriptionsForCustomerAsync(customerId, cancellationToken).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------------------------
    // Plans
    // ---------------------------------------------------------------------------------------

    private async Task<IReadOnlyCollection<SubscriptionPlan>> FetchPlansAsync(CancellationToken cancellationToken)
    {
        var familyId = "handle:" + ProductFamilyHandle;
        var plans = new List<SubscriptionPlan>();

        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<ProductResponse> pageItems;
            using (var call = MaxioCallContext.BeginRead())
            {
                var pageNumber = page;
                try
                {
                    pageItems = await InvokeAsync(
                        "ListProductsForProductFamily",
                        call,
                        token => _client.ProductFamilies.ListProductsForProductFamily(
                            productFamilyId: familyId,
                            dateField: null,
                            filter: null,
                            startDate: null,
                            endDate: null,
                            startDatetime: null,
                            endDatetime: null,
                            includeArchived: false,
                            include: null,
                            page: pageNumber,
                            perPage: PlanPageSize,
                            ct: token),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (SdkException<ListProductsForProductFamilyError> ex)
                {
                    if (ex.Error.TryGetString(out var notFoundBody))
                    {
                        _logger.LogError("Maxio product family {FamilyHandle} was not found: {Body}",
                            ProductFamilyHandle, Truncate(notFoundBody));
                        throw new BillingConfigurationException(
                            "The configured Maxio product family '" + ProductFamilyHandle + "' does not exist.", ex);
                    }

                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw Translate("ListProductsForProductFamily", call, raw, ex);
                    }

                    throw new BillingException("The billing provider returned an unexpected error while listing plans.", ex);
                }
            }

            plans.AddRange(pageItems
                .Select(item => item.Product)
                .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
                .Select(MapPlan));

            if (pageItems.Count < PlanPageSize)
            {
                break;
            }
        }

        return plans
            .OrderBy(plan => plan.PriceInCents ?? long.MaxValue)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string? requestedHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken).ConfigureAwait(false);
        var handle = requestedHandle ?? _settings.DefaultProductHandle;

        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingValidationException(
                "No plan was requested and no default plan is configured (" + MaxioSettings.SectionName +
                ":DefaultProductHandle). Choose one of: " + DescribeAvailable(plans) + ".");
        }

        var plan = plans.FirstOrDefault(candidate =>
            string.Equals(candidate.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (plan is null)
        {
            throw new BillingNotFoundException(
                "Subscription plan '" + handle + "' was not found in product family '" + ProductFamilyHandle +
                "'. Available plans: " + DescribeAvailable(plans) + ".");
        }

        return plan;
    }

    private static string DescribeAvailable(IReadOnlyCollection<SubscriptionPlan> plans) =>
        plans.Count == 0 ? "(none)" : string.Join(", ", plans.Select(plan => plan.Handle));

    // ---------------------------------------------------------------------------------------
    // Customers
    // ---------------------------------------------------------------------------------------

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var call = MaxioCallContext.BeginRead();
        try
        {
            var response = await InvokeAsync(
                "ReadCustomerByReference",
                call,
                token => _client.Customers.ReadCustomerByReference(reference, ct: token),
                cancellationToken).ConfigureAwait(false);

            return response.Customer;
        }
        catch (BillingNotFoundException)
        {
            // A genuine miss. Note that an unreadable body never reaches here - it is translated
            // as an unavailable/unknown outcome, so a corrupt response can never be mistaken for
            // "this shopper has no billing customer" and trigger a spurious create.
            return null;
        }
    }

    private async Task<int> EnsureCustomerAsync(SubscriberIdentity subscriber, string reference, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(reference, cancellationToken).ConfigureAwait(false);
        if (existing?.Id is int existingId)
        {
            return existingId;
        }

        var (firstName, lastName) = ResolveName(subscriber);

        using var call = MaxioCallContext.BeginWrite();
        try
        {
            var response = await InvokeAsync(
                "CreateCustomer",
                call,
                token => _client.Customers.CreateCustomer(
                    body: new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = subscriber.Email,
                            Reference = reference
                        }
                    },
                    ct: token),
                cancellationToken).ConfigureAwait(false);

            var customerId = response.Customer.Id;
            if (customerId is not int created)
            {
                throw new BillingOutcomeUnknownException(
                    "The billing provider accepted the customer but did not return its identifier.");
            }

            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.", created, reference);
            return created;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // Maxio enforces uniqueness on the customer reference, so a 422 here is the expected
            // shape of a lost race: another request created this customer first. Re-look it up
            // before treating the rejection as a failure.
            if (ex.Error.TryGetCustomerErrorResponse1(out var customerError))
            {
                var winner = await FindCustomerAsync(reference, cancellationToken).ConfigureAwait(false);
                if (winner?.Id is int wonId)
                {
                    _logger.LogInformation(
                        "Maxio rejected a duplicate customer create for {CustomerReference}; using existing customer {CustomerId}.",
                        reference, wonId);
                    return wonId;
                }

                var details = ReadCustomerErrors(customerError);
                _logger.LogWarning(ex, "Maxio rejected the customer create for {CustomerReference}: {Details}", reference, details);
                throw new BillingValidationException(
                    "The billing provider rejected the customer record for this account." + Suffix(details),
                    details,
                    ex,
                    (int)HttpStatusCode.UnprocessableEntity);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate("CreateCustomer", call, raw, ex);
            }

            throw new BillingException("The billing provider returned an unexpected error while creating the customer.", ex);
        }
        catch (BillingOutcomeUnknownException)
        {
            // The create may have been received. Settle it by re-reading provider state rather
            // than assuming nothing happened.
            var reconciled = await FindCustomerAsync(reference, cancellationToken).ConfigureAwait(false);
            if (reconciled?.Id is int reconciledId)
            {
                _logger.LogWarning(
                    "Customer create for {CustomerReference} had an unknown outcome but reconciled to customer {CustomerId}.",
                    reference, reconciledId);
                return reconciledId;
            }

            throw;
        }
    }

    private static (string FirstName, string LastName) ResolveName(SubscriberIdentity subscriber)
    {
        var first = subscriber.FirstName?.Trim();
        var last = subscriber.LastName?.Trim();

        if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(last))
        {
            return (first!, last!);
        }

        // Maxio requires both names. eShopOnWeb identities carry only a login name, so derive
        // something recognisable from the email local part rather than sending a blank.
        var local = subscriber.Email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var derivedFirst = first;
        var derivedLast = last;

        if (string.IsNullOrEmpty(derivedFirst))
        {
            derivedFirst = Titleize(parts.Length > 0 ? parts[0] : local);
        }

        if (string.IsNullOrEmpty(derivedLast))
        {
            derivedLast = parts.Length > 1 ? Titleize(parts[parts.Length - 1]) : "eShopOnWeb";
        }

        return (derivedFirst!, derivedLast!);
    }

    private static string Titleize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "eShopOnWeb";
        }

        return value.Length == 1
            ? value.ToUpperInvariant()
            : char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    private static IReadOnlyCollection<string> ReadCustomerErrors(CustomerErrorResponse1 customerError)
    {
        // The generated payload for this status reuses a shared model whose only members are
        // unrelated to customer validation, so treat anything found here as best-effort detail
        // and never let its absence become an unhandled null.
        try
        {
            var messages = new List<string>();
            var errors = customerError.Errors;
            if (errors?.PerPage is { Count: > 0 } perPage)
            {
                messages.AddRange(perPage);
            }

            if (errors?.PricePoint is { Count: > 0 } pricePoint)
            {
                messages.AddRange(pricePoint);
            }

            return messages;
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Subscriptions
    // ---------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken)
    {
        using var call = MaxioCallContext.BeginRead();

        var response = await InvokeAsync(
            "ListCustomerSubscriptions",
            call,
            token => _client.Customers.ListCustomerSubscriptions(customerId, ct: token),
            cancellationToken).ConfigureAwait(false);

        return response
            .Select(item => item.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => MapSubscription(subscription!))
            .OrderByDescending(subscription => subscription.Id ?? 0)
            .ToArray();
    }

    private static string NextSubscriptionReference(
        string customerReference,
        string planHandle,
        IReadOnlyCollection<CustomerSubscription> existing)
    {
        var taken = new HashSet<string>(
            existing.Select(subscription => subscription.Reference).Where(reference => reference is not null)!,
            StringComparer.OrdinalIgnoreCase);

        for (var attempt = 1; attempt <= MaxReferenceAttempts; attempt++)
        {
            var candidate = MaxioReference.ForSubscription(customerReference, planHandle, attempt);
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new BillingValidationException(
            "This account has re-subscribed to '" + planHandle + "' too many times to derive a new billing reference.");
    }

    private async Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        SubscriptionPlan plan,
        string reference,
        CancellationToken cancellationToken)
    {
        var planHandle = plan.Handle;
        var collectionMethod = ResolveCollectionMethod(plan);

        using var call = MaxioCallContext.BeginWrite();
        try
        {
            var response = await InvokeAsync(
                "CreateSubscription",
                call,
                token => _client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerId = customerId,

                            // Our own idempotency key, echoed back by the provider and used to
                            // reconcile a write whose outcome was lost. NOT the SDK's 'Ref'
                            // member, which is a referral code and fails the call when invalid.
                            Reference = reference,

                            // For a plan that needs no stored payment method this asks for a
                            // collection method that does not demand one. Leaving it unset makes
                            // the site default (automatic) apply, and the provider then rejects
                            // the signup with "No payment method was on file" - so this is what
                            // lets a card-less plan be subscribed to without card capture.
                            PaymentCollectionMethod = collectionMethod
                        }
                    },
                    ct: token),
                cancellationToken).ConfigureAwait(false);

            if (response.Subscription is null)
            {
                throw new BillingOutcomeUnknownException(
                    "The billing provider accepted the subscription but returned no subscription record.");
            }

            return MapSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var details = ReadSubscriptionErrors(errorList);

                // Surfaced verbatim on purpose: if the site ever demands a payment collection
                // method for these plans, this message is the only signal that says so.
                _logger.LogError(ex, "Maxio rejected the subscription create for {Reference} on {PlanHandle}: {Details}",
                    reference, planHandle, string.Join("; ", details));

                throw new BillingValidationException(
                    "The billing provider rejected the subscription." + Suffix(details),
                    details,
                    ex,
                    (int)HttpStatusCode.UnprocessableEntity);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate("CreateSubscription", call, raw, ex);
            }

            throw new BillingException("The billing provider returned an unexpected error while creating the subscription.", ex);
        }
        catch (BillingOutcomeUnknownException)
        {
            var reconciled = await FindSubscriptionByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
            if (reconciled is not null)
            {
                _logger.LogWarning(
                    "Subscription create for {Reference} had an unknown outcome but reconciled to subscription {SubscriptionId}.",
                    reference, reconciled.Id);
                return reconciled;
            }

            throw;
        }
    }

    private async Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var call = MaxioCallContext.BeginRead();
        try
        {
            var response = await InvokeAsync(
                "FindSubscription",
                call,
                token => _client.Subscriptions.FindSubscription(reference, ct: token),
                cancellationToken).ConfigureAwait(false);

            return response.Subscription is null ? null : MapSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate("FindSubscription", call, raw, ex);
            }

            throw new BillingException("The billing provider returned an unexpected error while looking up the subscription.", ex);
        }
        catch (BillingNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Chooses how the provider should collect payment for this plan. An explicit configuration
    /// value wins; otherwise it is derived from what the provider itself reports about the plan,
    /// so the same build works against a catalogue whose plans do require a card.
    /// </summary>
    private CollectionMethod? ResolveCollectionMethod(SubscriptionPlan plan)
    {
        if (!string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod))
        {
            return MapCollectionMethod(_settings.PaymentCollectionMethod!);
        }

        // A plan that requires a payment method is left to the site default, because asking for a
        // card-less collection method there would silently change how the shopper is billed.
        return plan.RequiresPaymentMethod ? null : CollectionMethod.Remittance;
    }

    private static CollectionMethod? MapCollectionMethod(string value) => value.Trim().ToLowerInvariant() switch
    {
        "automatic" => CollectionMethod.Automatic,
        "remittance" => CollectionMethod.Remittance,
        "invoice" => CollectionMethod.Invoice,
        "prepaid" => CollectionMethod.Prepaid,
        _ => null
    };

    private static IReadOnlyCollection<string> ReadSubscriptionErrors(ErrorListResponse1 errorList)
    {
        try
        {
            return errorList.Errors?.ToArray() ?? Array.Empty<string>();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Mapping
    // ---------------------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
        ProductFamilyHandle = product.ProductFamily?.Handle,
        ProductFamilyName = product.ProductFamily?.Name,
        RequiresPaymentMethod = product.RequireCreditCard ?? false,
        HasTrial = (product.TrialInterval ?? 0) > 0 || (product.TrialPriceInCents ?? 0) > 0,
        SetupFeeInCents = product.InitialChargeInCents ?? 0
    };

    private static CustomerSubscription MapSubscription(Subscription subscription)
    {
        var state = subscription.State?.Value;

        return new CustomerSubscription
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = state,
            IsLive = IsLive(state),
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            Interval = subscription.Product?.Interval,
            IntervalUnit = subscription.Product?.IntervalUnit?.Value,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt
        };
    }

    private static bool IsLive(string? state) => state is null || !EndedStates.Contains(state);

    // ---------------------------------------------------------------------------------------
    // Error boundary
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Runs one SDK call under the total call budget and converts everything that is not a typed
    /// per-operation error into a <see cref="BillingException"/>. Typed
    /// <c>SdkException&lt;{Operation}Error&gt;</c> instances deliberately pass through: their
    /// accessors only exist on the concrete error type, so they are read at the call site.
    /// </summary>
    private async Task<T> InvokeAsync<T>(
        string operation,
        MaxioCallContext call,
        Func<CancellationToken, Task<T>> body,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_settings.CallBudget > TimeSpan.Zero)
        {
            budget.CancelAfter(_settings.CallBudget);
        }

        try
        {
            return await body(budget.Token).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(operation, call, ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw TranslateUnreadableBody(operation, call, ex);
        }
        catch (MaxioResendBlockedException ex)
        {
            _logger.LogError(ex, "Maxio {Operation} was not re-sent after a transport failure; its outcome is unknown.", operation);
            throw new BillingOutcomeUnknownException(
                "The request reached the billing provider but its outcome could not be confirmed. Check your subscriptions before retrying.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw TranslateTransportFailure(operation, call, ex, timedOut: false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw TranslateTransportFailure(operation, call, ex, timedOut: true);
        }
    }

    private BillingException Translate(string operation, MaxioCallContext call, RawError raw, Exception ex)
    {
        var status = (int)raw.StatusCode;
        string? bodyText = null;

        try
        {
            bodyText = raw.ReadAsString();
        }
        catch (Exception readFailure)
        {
            _logger.LogDebug(readFailure, "Could not read the Maxio error body for {Operation}.", operation);
        }

        return TranslateStatus(operation, call, status, bodyText, ex);
    }

    private BillingException TranslateStatus(string operation, MaxioCallContext call, int status, string? body, Exception ex)
    {
        _logger.LogWarning("Maxio {Operation} returned HTTP {StatusCode}: {Body}", operation, status, Truncate(body));

        switch (status)
        {
            case 401:
            case 403:
                return new BillingConfigurationException(
                    "The billing provider rejected the configured credentials.", ex);

            case 404:
                return new BillingNotFoundException(
                    "The requested billing record was not found.", ex, status);

            case 400:
            case 409:
            case 422:
                var detail = Truncate(body);
                return new BillingValidationException(
                    "The billing provider rejected the request." + (detail is null ? string.Empty : " " + detail),
                    detail is null ? null : new[] { detail },
                    ex,
                    status);

            case 429:
                return new BillingUnavailableException(
                    "The billing provider is rate limiting requests. Please try again shortly.", ex, status);

            default:
                if (status >= 500)
                {
                    // A failed write is not automatically a no-op: the provider may have applied
                    // it before failing, so a write gets the unknown-outcome type (which the
                    // caller reconciles) while a read is simply unavailable.
                    return call.WriteOnce
                        ? new BillingOutcomeUnknownException(
                            "The billing provider failed while processing the request; its outcome is unknown.", ex, status)
                        : new BillingUnavailableException(
                            "The billing provider reported an error. Please try again.", ex, status);
                }

                return new BillingException("The billing provider returned an unexpected response.", ex, status);
        }
    }

    private BillingException TranslateUnreadableBody(string operation, MaxioCallContext call, JsonException ex)
    {
        // Two very different failures arrive as the same exception type.
        if (call.LastStatusCode is { } status && !call.LastResponseWasSuccess)
        {
            // The provider rejected the request and only the DETAIL was lost. Reporting this as an
            // outage would tell a retrying caller to keep retrying something that cannot succeed,
            // so the recorded status decides the outcome instead.
            _logger.LogError(ex, "Maxio {Operation} returned HTTP {StatusCode} with a body the SDK could not parse.",
                operation, (int)status);
            return TranslateStatus(operation, call, (int)status, body: null, ex);
        }

        _logger.LogError(ex, "Maxio {Operation} returned a response that could not be processed.", operation);

        return call.WriteOnce
            ? new BillingOutcomeUnknownException(
                "The billing provider accepted the request but returned a response that could not be processed. Check your subscriptions before retrying.", ex)
            : new BillingUnavailableException(
                "The billing provider returned a response that could not be processed.", ex);
    }

    private BillingException TranslateTransportFailure(string operation, MaxioCallContext call, Exception ex, bool timedOut)
    {
        var reason = timedOut ? "timed out" : "could not be reached";

        if (call.WriteOnce && call.SendCount > 0)
        {
            _logger.LogError(ex, "Maxio {Operation} {Reason} after {SendCount} send(s); its outcome is unknown.",
                operation, reason, call.SendCount);
            return new BillingOutcomeUnknownException(
                "The request was sent to the billing provider but its outcome could not be confirmed. Check your subscriptions before retrying.", ex);
        }

        _logger.LogWarning(ex, "Maxio {Operation} {Reason}.", operation, reason);
        return new BillingUnavailableException("The billing provider " + reason + ". Please try again.", ex);
    }

    private static string Suffix(IReadOnlyCollection<string> details) =>
        details.Count == 0 ? string.Empty : " " + string.Join(" ", details);

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaxProviderMessageLength
            ? trimmed
            : trimmed.Substring(0, MaxProviderMessageLength) + "...";
    }
}
