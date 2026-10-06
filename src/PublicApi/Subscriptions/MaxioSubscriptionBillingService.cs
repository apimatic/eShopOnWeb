using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing (system of record).
/// All provider failures are translated into <see cref="MaxioBillingException"/> with a
/// caller-safe message; the shopper's email (from the JWT) is the stable anchor mapped to a
/// Maxio customer via the provider-enforced unique customer reference.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    internal const string CustomerReferencePrefix = "eshoponweb-";

    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int PlanPageSize = 100;
    private const int MaxPlanPages = 20;

    private static readonly SubscriptionState[] DeadStates =
    [
        SubscriptionState.Canceled,
        SubscriptionState.Expired,
        SubscriptionState.FailedToCreate,
        SubscriptionState.TrialEnded,
    ];

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    // Serializes concurrent subscribe attempts for the same shopper+plan in this process,
    // closing the check-then-create race window that a double-click opens.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeGates = new();

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var productFamilyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var plans = new List<SubscriptionPlanDto>();

        for (var page = 1; ; page++)
        {
            var pageItems = await CallAsync(
                "list plans",
                token => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: productFamilyId,
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
                    ct: token),
                cancellationToken);

            foreach (var wrapper in pageItems)
            {
                if (wrapper?.Product is { } product)
                {
                    plans.Add(MapPlan(product));
                }
            }

            if (pageItems.Count < PlanPageSize)
            {
                break;
            }

            if (page >= MaxPlanPages)
            {
                _logger.LogWarning(
                    "Maxio plan listing stopped after {Page} pages for family {ProductFamilyId}; results may be truncated.",
                    page, productFamilyId);
                break;
            }
        }

        return plans;
    }

    public async Task<SubscribeOutcome> SubscribeAsync(string email, string planHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var normalizedEmail = RequireEmail(email);
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioBillingException(
                MaxioBillingFailureKind.Rejected,
                "A plan handle is required to subscribe.",
                providerStatusCode: HttpStatusCode.BadRequest);
        }

        planHandle = planHandle.Trim();
        var customerReference = CustomerReferenceFor(normalizedEmail);
        var planKey = $"{customerReference}|{planHandle.ToLowerInvariant()}";

        var gate = _subscribeGates.GetOrAdd(planKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customerId = await EnsureCustomerIdAsync(normalizedEmail, customerReference, cancellationToken);

            var product = await GetProductByHandleAsync(planHandle, cancellationToken);
            if (product.RequireCreditCard == true)
            {
                throw new MaxioBillingException(
                    MaxioBillingFailureKind.Conflict,
                    "This plan requires a payment method, which subscription signup in this store does not support yet.",
                    providerStatusCode: HttpStatusCode.Conflict);
            }

            var existing = await FindLiveSubscriptionForPlanAsync(customerId, customerReference, planHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Shopper {Email} already holds a live subscription to {Plan}; returning subscription {SubscriptionId} instead of creating a duplicate.",
                    normalizedEmail, planHandle, existing.Id);
                return new SubscribeOutcome { Subscription = existing, AlreadySubscribed = true };
            }

            var signupReference =
                $"{customerReference}:{planHandle.ToLowerInvariant()}:{Guid.NewGuid().ToString("N").Substring(0, 8)}";

            SubscriptionResponse created;
            try
            {
                created = await CallAsync(
                    "create subscription",
                    async token =>
                    {
                        using (MaxioWriteGuardHandler.BeginWriteScope())
                        {
                            return await _client.Subscriptions.CreateSubscription(
                                new CreateSubscriptionRequest
                                {
                                    Subscription = new CreateSubscription
                                    {
                                        CustomerId = customerId,
                                        ProductHandle = planHandle,
                                        Reference = signupReference,
                                        // Card-less signup: collect by remittance so enrollment does not
                                        // require a payment profile. The live sandbox rejects automatic
                                        // collection with 422 "no payment method on file" for these plans.
                                        PaymentCollectionMethod = CollectionMethod.Remittance,
                                    },
                                },
                                ct: token);
                        }
                    },
                    cancellationToken);
            }
            catch (Exception ex) when (IsUnknownWriteOutcome(ex))
            {
                // The create may or may not have reached the provider (transport retry, blocked
                // re-send, timeout, unreadable body). Settle the outcome by re-reading provider
                // state instead of assuming nothing happened.
                _logger.LogWarning(
                    ex,
                    "Subscription create outcome unknown for {Email}/{Plan}; reconciling from provider state.",
                    normalizedEmail, planHandle);

                SubscriptionDto? reconciled = null;
                try
                {
                    reconciled = await FindLiveSubscriptionForPlanAsync(
                        customerId, customerReference, planHandle, cancellationToken);
                }
                catch (Exception reconcileException)
                {
                    _logger.LogWarning(
                        reconcileException,
                        "Reconciling read for {Email}/{Plan} failed; surfacing the original create error.",
                        normalizedEmail, planHandle);
                }

                if (reconciled is not null)
                {
                    return new SubscribeOutcome { Subscription = reconciled, AlreadySubscribed = true };
                }

                throw;
            }

            var subscription = created?.Subscription
                ?? throw new MaxioBillingException(
                    MaxioBillingFailureKind.UnparseableProviderResponse,
                    "The billing provider accepted the subscription but returned no subscription data.");

            return new SubscribeOutcome
            {
                Subscription = MapSubscription(subscription, planHandle),
                AlreadySubscribed = false,
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsForShopperAsync(
        string email, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var normalizedEmail = RequireEmail(email);
        var customerReference = CustomerReferenceFor(normalizedEmail);

        int customerId;
        try
        {
            var customer = await CallAsync(
                "read customer",
                token => _client.Customers.ReadCustomerByReference(customerReference, ct: token),
                cancellationToken);
            customerId = RequireCustomerId(customer);
        }
        catch (MaxioBillingException ex) when (ex.ProviderStatusCode == HttpStatusCode.NotFound)
        {
            // The shopper never subscribed — no customer exists yet. That is an empty list, not an error.
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await CallAsync(
            "list customer subscriptions",
            token => _client.Customers.ListCustomerSubscriptions(customerId, ct: token),
            cancellationToken);

        var results = new List<SubscriptionDto>();
        foreach (var wrapper in subscriptions)
        {
            if (wrapper?.Subscription is { } subscription)
            {
                results.Add(MapSubscription(
                    subscription,
                    TryExtractPlanHandle(subscription.Reference, customerReference)));
            }
        }

        return results;
    }

    private async Task<int> EnsureCustomerIdAsync(
        string normalizedEmail, string customerReference, CancellationToken cancellationToken)
    {
        try
        {
            var found = await CallAsync(
                "read customer",
                token => _client.Customers.ReadCustomerByReference(customerReference, ct: token),
                cancellationToken);
            return RequireCustomerId(found);
        }
        catch (MaxioBillingException ex) when (ex.ProviderStatusCode == HttpStatusCode.NotFound)
        {
            try
            {
                var (firstName, lastName) = BuildCustomerNames(normalizedEmail);
                var created = await CallAsync(
                    "create customer",
                    async token =>
                    {
                        using (MaxioWriteGuardHandler.BeginWriteScope())
                        {
                            return await _client.Customers.CreateCustomer(
                                new CreateCustomerRequest
                                {
                                    Customer = new CreateCustomer
                                    {
                                        FirstName = firstName,
                                        LastName = lastName,
                                        Email = normalizedEmail,
                                        Reference = customerReference,
                                    },
                                },
                                ct: token);
                        }
                    },
                    cancellationToken);
                return RequireCustomerId(created);
            }
            catch (Exception innerException) when (IsCreateCustomerConvergenceCandidate(innerException))
            {
                // The provider enforces one customer per reference value, so a rejection (or an
                // unknown outcome) most likely means a concurrent signup won the race. Converge
                // by re-reading; only if it is genuinely absent is the original error the truth.
                try
                {
                    var converged = await CallAsync(
                        "read customer",
                        token => _client.Customers.ReadCustomerByReference(customerReference, ct: token),
                        cancellationToken);
                    _logger.LogInformation(
                        "Converged on existing Maxio customer for reference {Reference} after create race/failure.",
                        customerReference);
                    return RequireCustomerId(converged);
                }
                catch (MaxioBillingException rereadException) when (rereadException.ProviderStatusCode == HttpStatusCode.NotFound)
                {
                    throw innerException;
                }
            }
        }
    }

    private async Task<Product> GetProductByHandleAsync(string planHandle, CancellationToken cancellationToken)
    {
        var response = await CallAsync(
            "read plan",
            token => _client.Products.ReadProductByHandle(planHandle, ct: token),
            cancellationToken);

        return response?.Product
            ?? throw new MaxioBillingException(
                MaxioBillingFailureKind.UnparseableProviderResponse,
                "The billing provider returned a plan response without plan data.");
    }

    private async Task<SubscriptionDto?> FindLiveSubscriptionForPlanAsync(
        int customerId, string customerReference, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await CallAsync(
            "list customer subscriptions",
            token => _client.Customers.ListCustomerSubscriptions(customerId, ct: token),
            cancellationToken);

        var referencePrefix = $"{customerReference}:{planHandle.ToLowerInvariant()}:";

        foreach (var wrapper in subscriptions)
        {
            var subscription = wrapper?.Subscription;
            if (subscription is null || !IsLiveState(subscription.State))
            {
                continue;
            }

            var matchesPlan =
                string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) ||
                subscription.Reference?.StartsWith(referencePrefix, StringComparison.Ordinal) == true;

            if (!matchesPlan)
            {
                continue;
            }

            // The nested product is not guaranteed in list payloads; enrich best-effort.
            if (subscription.Product is null && subscription.Id is { } subscriptionId)
            {
                try
                {
                    var detail = await CallAsync(
                        "read subscription",
                        token => _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: token),
                        cancellationToken);
                    subscription = detail?.Subscription ?? subscription;
                }
                catch (MaxioBillingException ex)
                {
                    _logger.LogWarning(ex, "Could not enrich subscription {SubscriptionId} with plan details.", subscriptionId);
                }
            }

            return MapSubscription(subscription, planHandle);
        }

        return null;
    }

    private async Task<string> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        var handle = _options.ProductFamilyHandle!.Trim();

        var families = await CallAsync(
            "list product families",
            token => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: token),
            cancellationToken);

        var family = families?
            .FirstOrDefault(f => string.Equals(f?.ProductFamily?.Handle, handle, StringComparison.OrdinalIgnoreCase))
            ?.ProductFamily;

        if (family?.Id is null)
        {
            _logger.LogError("Maxio product family with handle {Handle} was not found; check Maxio:ProductFamilyHandle.", handle);
            throw new MaxioBillingException(
                MaxioBillingFailureKind.Configuration,
                "The billing catalog is not configured correctly on this server.");
        }

        // Numeric ids are not stable across re-seeds; always resolve them at runtime.
        return family.Id.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static SubscriptionPlanDto MapPlan(Product product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        PriceInCents = product.PriceInCents,
        Price = product.PriceInCents is { } cents ? cents / 100m : null,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
        TrialPriceInCents = product.TrialPriceInCents,
        RequiresPaymentMethod = product.RequireCreditCard,
    };

    private static SubscriptionDto MapSubscription(Subscription subscription, string? planHandleFallback) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State?.Value,
        IsLive = IsLiveState(subscription.State),
        PlanHandle = subscription.Product?.Handle ?? planHandleFallback,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CurrentPeriodStartsAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt,
    };

    private static bool IsLiveState(SubscriptionState? state) =>
        state is null || !DeadStates.Contains(state);

    private static bool IsUnknownWriteOutcome(Exception ex) =>
        ex is MaxioResendBlockedException ||
        (ex is MaxioBillingException mbe &&
         mbe.Kind is MaxioBillingFailureKind.ProviderUnavailable
            or MaxioBillingFailureKind.Timeout
            or MaxioBillingFailureKind.UnparseableProviderResponse);

    private static bool IsCreateCustomerConvergenceCandidate(Exception ex) =>
        ex is MaxioResendBlockedException ||
        (ex is MaxioBillingException mbe &&
         mbe.Kind is MaxioBillingFailureKind.Rejected
            or MaxioBillingFailureKind.ProviderUnavailable
            or MaxioBillingFailureKind.Timeout
            or MaxioBillingFailureKind.UnparseableProviderResponse);

    private static string? TryExtractPlanHandle(string? reference, string customerReference)
    {
        if (reference is null)
        {
            return null;
        }

        var prefix = customerReference + ":";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = reference.Substring(prefix.Length);
        var separator = rest.IndexOf(':');
        return separator > 0 ? rest.Substring(0, separator) : rest;
    }

    /// <summary>
    /// Deterministic, URL-safe Maxio customer reference for a shopper. The provider enforces
    /// one customer per reference value, so this is the idempotency anchor — no local mapping
    /// table can drift from it, and it survives app restarts (in-memory identity included).
    /// </summary>
    private static string CustomerReferenceFor(string normalizedEmail)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail))).ToLowerInvariant();
        return CustomerReferencePrefix + hash.Substring(0, 40);
    }

    private static (string FirstName, string LastName) BuildCustomerNames(string email)
    {
        var localPart = email.Contains('@', StringComparison.Ordinal) ? email.Substring(0, email.IndexOf('@')) : email;
        var tokens = localPart
            .Split(['.', '_', '-', '+'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Titlecase)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();

        var firstName = tokens.Length > 0 ? tokens[0] : "Shopper";
        var lastName = tokens.Length > 1 ? string.Join(" ", tokens.Skip(1)) : "eShopOnWeb Customer";
        return (firstName, lastName);
    }

    private static string Titlecase(string value) =>
        string.IsNullOrEmpty(value)
            ? value
            : char.ToUpper(value[0], CultureInfo.InvariantCulture) +
              value.Substring(1).ToLower(CultureInfo.InvariantCulture);

    private static string RequireEmail(string email)
    {
        var normalized = email?.Trim().ToLower(CultureInfo.InvariantCulture) ?? string.Empty;
        if (normalized.Length == 0 || !normalized.Contains('@', StringComparison.Ordinal))
        {
            throw new MaxioBillingException(
                MaxioBillingFailureKind.Rejected,
                "A valid shopper email is required.",
                providerStatusCode: HttpStatusCode.BadRequest);
        }

        return normalized;
    }

    private static int RequireCustomerId(CustomerResponse response)
    {
        if (response?.Customer?.Id is not { } id)
        {
            throw new MaxioBillingException(
                MaxioBillingFailureKind.UnparseableProviderResponse,
                "The billing provider returned a customer response without a customer id.");
        }

        return id;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new MaxioBillingException(
                MaxioBillingFailureKind.Configuration,
                "The billing provider is not configured on this server.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioBillingException(
                MaxioBillingFailureKind.Configuration,
                "The billing provider is not configured on this server.");
        }

        if (string.IsNullOrWhiteSpace(_options.Subdomain) && string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new MaxioBillingException(
                MaxioBillingFailureKind.Configuration,
                "The billing provider is not configured on this server.");
        }
    }

    /// <summary>
    /// The one boundary every provider call crosses: a whole-call budget (the retry policy and
    /// HttpClient timeouts are per-attempt), plus translation of every SDK failure shape into a
    /// single <see cref="MaxioBillingException"/> carrying an HTTP status and caller-safe message.
    /// </summary>
    private async Task<T> CallAsync<T>(string operation, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        using var transport = MaxioRequestLoggingHandler.Observe();
        try
        {
            return await call(budget.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(operation, ex);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw MapCreateCustomerError(operation, ex);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw MapCreateSubscriptionError(operation, ex);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw MapListProductsError(operation, ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // the caller (HTTP request) went away — nothing to map
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Maxio call {Operation} exceeded the {Budget} budget.", operation, CallBudget);
            throw new MaxioBillingException(
                MaxioBillingFailureKind.Timeout,
                $"The billing provider did not respond within {(int)CallBudget.TotalSeconds} seconds.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Maxio call {Operation} failed at the transport level.", operation);
            throw new MaxioBillingException(
                MaxioBillingFailureKind.ProviderUnavailable,
                "The billing provider is currently unreachable.",
                ex);
        }
        catch (JsonException ex)
        {
            throw MapJsonParseFailure(operation, ex, transport.LastStatus);
        }
    }

    private MaxioBillingException MapRaw(string operation, SdkException<RawError> ex)
    {
        var body = SafeReadBody(ex.Error);
        _logger.LogWarning(
            "Maxio {Operation} returned HTTP {StatusCode}: {Body}",
            operation, (int)ex.Error.StatusCode, Truncate(body, 500));
        return BuildFromProviderStatus(operation, ex.Error.StatusCode, ExtractProviderErrors(body));
    }

    private MaxioBillingException MapCreateCustomerError(string operation, SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var typed))
        {
            var messages =
                (typed?.Errors?.PerPage ?? Array.Empty<string>())
                .Concat(typed?.Errors?.PricePoint ?? Array.Empty<string>())
                .ToList();
            _logger.LogWarning(
                "Maxio {Operation} rejected the customer (422). Provider messages: {Messages}",
                operation, string.Join("; ", messages));
            return new MaxioBillingException(
                MaxioBillingFailureKind.Rejected,
                "The billing provider rejected the customer details.",
                providerStatusCode: HttpStatusCode.UnprocessableEntity,
                providerErrors: messages);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return BuildFromProviderStatus(operation, raw.StatusCode, ExtractProviderErrors(SafeReadBody(raw)));
        }

        return new MaxioBillingException(
            MaxioBillingFailureKind.Rejected,
            "The billing provider rejected the customer details.",
            providerStatusCode: HttpStatusCode.UnprocessableEntity);
    }

    private MaxioBillingException MapCreateSubscriptionError(string operation, SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var typed) && typed?.Errors is { } errors)
        {
            var messages = errors.ToList();
            _logger.LogWarning(
                "Maxio {Operation} rejected the subscription (422). Provider messages: {Messages}",
                operation, string.Join("; ", messages));
            return new MaxioBillingException(
                MaxioBillingFailureKind.Rejected,
                "The billing provider rejected the subscription request.",
                providerStatusCode: HttpStatusCode.UnprocessableEntity,
                providerErrors: messages);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return BuildFromProviderStatus(operation, raw.StatusCode, ExtractProviderErrors(SafeReadBody(raw)));
        }

        return new MaxioBillingException(
            MaxioBillingFailureKind.Rejected,
            "The billing provider rejected the subscription request.",
            providerStatusCode: HttpStatusCode.UnprocessableEntity);
    }

    private MaxioBillingException MapListProductsError(string operation, SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var text))
        {
            _logger.LogWarning(
                "Maxio {Operation} reported the product family as missing: {Text}", operation, Truncate(text, 500));
            throw new MaxioBillingException(
                MaxioBillingFailureKind.Configuration,
                "The billing catalog is not configured correctly on this server.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return BuildFromProviderStatus(operation, raw.StatusCode, ExtractProviderErrors(SafeReadBody(raw)));
        }

        return new MaxioBillingException(
            MaxioBillingFailureKind.ProviderUnavailable,
            "The billing provider returned an error.");
    }

    private MaxioBillingException MapJsonParseFailure(string operation, JsonException ex, int? wireStatus)
    {
        // A bare JsonException reaches this boundary from two opposite places:
        // - parsing a non-2xx body that does not match the generated error model: the request
        //   WAS rejected and only the reason was lost — never a 5xx, or callers retry forever;
        // - parsing a 2xx body that drifted from the model: outcome unknown — a 5xx.
        // The transport observation records the last wire status so the two stay distinguishable.
        _logger.LogError(
            ex,
            "JSON deserialization failed during Maxio {Operation}; last wire status {WireStatus}.",
            operation, wireStatus);

        if (wireStatus is >= 400 and < 500)
        {
            return new MaxioBillingException(
                MaxioBillingFailureKind.Rejected,
                "The billing provider rejected the request; the error detail could not be read.",
                ex,
                (HttpStatusCode)wireStatus.Value);
        }

        if (wireStatus >= 500)
        {
            return new MaxioBillingException(
                MaxioBillingFailureKind.ProviderUnavailable,
                "The billing provider returned an error.",
                ex,
                (HttpStatusCode)wireStatus.Value);
        }

        return new MaxioBillingException(
            MaxioBillingFailureKind.UnparseableProviderResponse,
            "The billing provider returned a response that could not be processed.",
            ex);
    }

    private MaxioBillingException BuildFromProviderStatus(
        string operation, HttpStatusCode status, IReadOnlyList<string> providerErrors)
    {
        if (status == HttpStatusCode.NotFound)
        {
            return new MaxioBillingException(
                MaxioBillingFailureKind.NotFound,
                $"{operation} was not found in the billing system.",
                providerStatusCode: status,
                providerErrors: providerErrors);
        }

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // Our credentials failing at the provider are never the caller's fault.
            _logger.LogError(
                "Maxio rejected our credentials with HTTP {StatusCode} — check Maxio:ApiKey / Maxio:Subdomain.",
                (int)status);
            return new MaxioBillingException(
                MaxioBillingFailureKind.Configuration,
                "The billing provider rejected this server's credentials.",
                providerStatusCode: status);
        }

        if ((int)status is >= 400 and < 500)
        {
            return new MaxioBillingException(
                MaxioBillingFailureKind.Rejected,
                "The billing provider rejected the request.",
                providerStatusCode: status,
                providerErrors: providerErrors);
        }

        return new MaxioBillingException(
            MaxioBillingFailureKind.ProviderUnavailable,
            "The billing provider returned an error.",
            providerStatusCode: status);
    }

    private static IReadOnlyList<string> ExtractProviderErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                if (errorsElement.ValueKind == JsonValueKind.Array)
                {
                    return errorsElement
                        .EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.GetRawText())
                        .ToList();
                }

                if (errorsElement.ValueKind == JsonValueKind.String)
                {
                    return new[] { errorsElement.GetString() ?? string.Empty };
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through to the truncated raw text.
        }

        var text = Truncate(body, 300);
        return string.IsNullOrEmpty(text) ? Array.Empty<string>() : new[] { text };
    }

    private static string? SafeReadBody(RawError error)
    {
        try
        {
            return error.ReadAsString();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value.Substring(0, max) + "...";
}
