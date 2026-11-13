using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod;
using Mx = MaxioAdvancedBilling.Models;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Subscription billing with Maxio Advanced Billing as the system of record.
/// <para>
/// Every write follows claim → SDK call → record: a primary-key claim row (<see cref="BillingClaimStore"/>) is
/// inserted before the provider is called, so a double-click is refused locally; the provider is then re-read
/// by the same deterministic reference before anything is created (covering restarts and other instances);
/// and an ambiguous write (connection drop, timeout, 5xx, unreadable 2xx) is settled by re-reading by
/// reference instead of being reported as a failure.
/// </para>
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>Total time one inbound request may spend on Maxio calls (the per-attempt bound is smaller).</summary>
    internal static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(30);

    /// <summary>Separate budget for settling an ambiguous write, so a write that used up the request budget can still be reconciled.</summary>
    internal static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(15);

    /// <summary>How long a request owns a pending claim before another request may take it over.</summary>
    internal static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    /// <summary>After an unconfirmed write, how long to wait before another request may re-check and retry it.</summary>
    internal static readonly TimeSpan UnknownOutcomeCoolDown = TimeSpan.FromSeconds(30);

    internal static readonly TimeSpan PlanCacheDuration = TimeSpan.FromMinutes(1);
    internal const int ProductPageSize = 200; // Maxio's per_page maximum
    internal const int MaxProductPages = 5;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly BillingClaimStore _claims;
    private readonly MaxioSiteArchitecture _site;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _time;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        BillingClaimStore claims,
        MaxioSiteArchitecture site,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        TimeProvider time,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _claims = claims;
        _site = site;
        _settings = settings.Value;
        _cache = cache;
        _time = time;
        _logger = logger;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ public surface

    public async Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = StartBudget(cancellationToken, RequestBudget);
        return await RunAsync("list subscription plans", () => GetPlanCatalogAsync(budget.Token), cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(BillingCustomerProfile customer, string planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, "A plan handle is required.");
        }

        using var budget = StartBudget(cancellationToken, RequestBudget);
        return await RunAsync("subscribe", async () =>
        {
            // Cross-operation invariant: only a handle the family listing returned may be subscribed to.
            var plan = await GetOfferedPlanAsync(planHandle, budget.Token);

            // Cross-operation invariant: the collection method must be valid for the site's invoicing architecture.
            // Resolved before any claim or write, so an unreadable site leaves nothing behind.
            var collectionMethod = await _site.GetInvoiceCollectionMethodAsync(budget.Token);

            var customerReference = MaxioReferences.ForCustomer(customer.UserName);
            var customerId = await EnsureCustomerAsync(customer, customerReference, budget.Token, cancellationToken);
            return await EnsureSubscriptionAsync(customerReference, customerId, plan, collectionMethod, budget.Token, cancellationToken);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        using var budget = StartBudget(cancellationToken, RequestBudget);
        return await RunAsync<IReadOnlyList<SubscriptionDetails>>("list customer subscriptions", async () =>
        {
            var token = budget.Token;
            var customerReference = MaxioReferences.ForCustomer(userName);

            // Never creates anything: a user with no billing customer simply has no subscriptions.
            var link = await _claims.FindAsync<BillingCustomerLink>(customerReference, token);
            long? customerId = link is { Status: BillingClaimStatus.Completed, ProviderId: long linkedId }
                ? linkedId
                : (await FindCustomerAsync(customerReference, token))?.Id;
            if (customerId is null)
            {
                return Array.Empty<SubscriptionDetails>();
            }

            var subscriptions = await ListSubscriptionsAsync(ToMaxioId(customerId.Value), token);
            await SettlePendingEnrollmentsAsync(customerReference, subscriptions, token);

            return subscriptions
                .Select(ToDetails)
                .OrderByDescending(s => s.CreatedAt)
                .ToList();
        }, cancellationToken);
    }

    // ------------------------------------------------------------------ plans

    /// <summary>The plan with exactly this handle from the family listing, or a caller-facing error.</summary>
    internal async Task<SubscriptionPlan> GetOfferedPlanAsync(string planHandle, CancellationToken token)
    {
        var catalog = await GetPlanCatalogAsync(token);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is not null)
        {
            return plan;
        }

        if (!catalog.IsComplete)
        {
            // The listing was cut short: the handle may exist beyond the cap. Never accept by default.
            throw new BillingException(BillingErrorKind.Unavailable, "The plan could not be verified right now. Please try again later.");
        }

        throw new BillingException(BillingErrorKind.InvalidRequest, $"'{planHandle}' is not an available subscription plan.");
    }

    private async Task<PlanCatalog> GetPlanCatalogAsync(CancellationToken token)
    {
        var familyHandle = _settings.ProductFamilyHandle!.Trim();
        var cacheKey = "maxio:plans:" + familyHandle;
        if (_cache.TryGetValue(cacheKey, out PlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var plans = new List<SubscriptionPlan>();
        var complete = false;
        for (var page = 1; page <= MaxProductPages; page++)
        {
            IReadOnlyList<Mx.ProductResponse> batch;
            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + familyHandle,
                        Page = page,
                        PerPage = ProductPageSize
                    },
                    cancellationToken: token);
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Typed ApiException<ListProductsForProductFamilyError> for a JSON-string body, or
                // ResponseDeserializationException for an empty/non-JSON 404 body: both mean "no such family".
                _logger.LogError(
                    "Maxio product family {ProductFamilyHandle} was not found (HTTP 404). Check the Maxio:ProductFamilyHandle setting.",
                    familyHandle);
                throw new BillingException(BillingErrorKind.ProviderFailure, "Subscription plans are not available right now.", null, ex);
            }

            foreach (var item in batch)
            {
                var product = item?.Product;
                if (product is null || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null)
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    string.IsNullOrWhiteSpace(product.Name) ? product.Handle : product.Name,
                    product.Description,
                    product.PriceInCents,
                    product.Interval,
                    product.IntervalUnit?.Value,
                    product.RequireCreditCard ?? false));
            }

            if (batch.Count < ProductPageSize)
            {
                complete = true;
                break;
            }
        }

        if (!complete)
        {
            _logger.LogWarning(
                "Maxio product family {ProductFamilyHandle} has more than {MaxProducts} products; the plan listing is incomplete.",
                familyHandle, ProductPageSize * MaxProductPages);
        }

        var catalog = new PlanCatalog(plans, complete);
        if (complete)
        {
            _cache.Set(cacheKey, catalog, PlanCacheDuration);
        }

        return catalog;
    }

    // ------------------------------------------------------------------ customer: find-or-create

    private async Task<int> EnsureCustomerAsync(BillingCustomerProfile profile, string customerReference, CancellationToken token, CancellationToken callerToken)
    {
        var now = UtcNow;
        var attempt = await _claims.TryClaimAsync(
            new BillingCustomerLink(customerReference, profile.UserName, now, ClaimLease), now, ClaimLease, token);
        var link = attempt.Claim;

        if (!attempt.Acquired)
        {
            if (link is { Status: BillingClaimStatus.Completed, ProviderId: long linkedId })
            {
                return ToMaxioId(linkedId);
            }

            throw InProgress();
        }

        // We hold the claim. Look before writing: the customer may already exist (earlier process, other instance).
        Mx.Customer? existing;
        try
        {
            existing = await FindCustomerAsync(customerReference, token);
        }
        catch
        {
            await ReleaseQuietlyAsync(link);
            throw;
        }

        if (existing is not null)
        {
            return await CompleteCustomerAsync(link, existing);
        }

        return await CreateCustomerAsync(profile, link, token, callerToken);
    }

    private async Task<int> CreateCustomerAsync(BillingCustomerProfile profile, BillingCustomerLink link, CancellationToken token, CancellationToken callerToken)
    {
        var request = new CreateCustomerOperationRequest
        {
            Body = new Mx.CreateCustomerRequest
            {
                Customer = new Mx.CreateCustomer
                {
                    FirstName = profile.FirstName,
                    LastName = profile.LastName,
                    Email = profile.Email,
                    Reference = link.Reference
                }
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(request, cancellationToken: token);
            if (response.Customer?.Id is null)
            {
                _logger.LogWarning("Maxio created customer {CustomerReference} but returned no id; reconciling by reference.", link.Reference);
                return await SettleCustomerAfterAmbiguousCreateAsync(link, null);
            }

            _logger.LogInformation("Created Maxio customer {MaxioCustomerId} for customer reference {CustomerReference}.", response.Customer.Id, link.Reference);
            return await CompleteCustomerAsync(link, response.Customer);
        }
        catch (ApiException<CreateCustomerError> ex) when (IsValidationRejection(ex.StatusCode))
        {
            var messages = ex.Error.Match(
                onCustomerErrorResponse1: DescribeCustomerErrors,
                otherwise: _ => (IReadOnlyList<string>)Array.Empty<string>());
            _logger.LogWarning(
                "Maxio rejected creating customer {CustomerReference} (HTTP {StatusCode}): {Errors}. Re-reading by reference before reporting.",
                link.Reference, (int)ex.StatusCode, string.Join("; ", messages));

            // The rejection may be "reference already taken" by a concurrent or earlier create — adopt that customer.
            var adopted = await TryFindCustomerForSettleAsync(link.Reference);
            if (adopted?.Id is not null)
            {
                return await CompleteCustomerAsync(link, adopted);
            }

            await ReleaseQuietlyAsync(link);
            throw new BillingException(BillingErrorKind.Rejected, "The billing service could not create your billing account.", messages, ex);
        }
        catch (Exception ex) when (IsUnknownOutcome(ex, callerToken))
        {
            _logger.LogError(ex, "Creating Maxio customer {CustomerReference} has an unknown outcome; reconciling by reference.", link.Reference);
            return await SettleCustomerAfterAmbiguousCreateAsync(link, ex);
        }
        catch (SdkException ex)
        {
            // Definitely not created (e.g. 401/403/429, unreadable 4xx): free the claim so a retry can proceed.
            await ReleaseQuietlyAsync(link);
            throw Translate("create customer", ex);
        }
    }

    /// <summary>
    /// Settles a customer create whose outcome is unknown. Maxio allows one customer per reference, so a
    /// definite "not found" means the create did not land and the claim can be freed for a retry.
    /// </summary>
    private async Task<int> SettleCustomerAfterAmbiguousCreateAsync(BillingCustomerLink link, Exception? cause)
    {
        Mx.Customer? found;
        try
        {
            using var settle = new CancellationTokenSource(SettleBudget);
            found = await FindCustomerAsync(link.Reference, settle.Token);
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException or BillingException)
        {
            _logger.LogError(ex, "Could not confirm whether Maxio customer {CustomerReference} was created; leaving the claim pending.", link.Reference);
            await MarkUnknownAsync(link);
            throw new BillingException(BillingErrorKind.Unavailable, "Your billing account could not be confirmed yet. Please try again in a moment.", null, cause ?? ex);
        }

        if (found?.Id is not null)
        {
            _logger.LogInformation("Reconciled Maxio customer {MaxioCustomerId} for customer reference {CustomerReference}.", found.Id, link.Reference);
            return await CompleteCustomerAsync(link, found);
        }

        await ReleaseQuietlyAsync(link);
        throw new BillingException(BillingErrorKind.Unavailable, "The billing service is temporarily unavailable. Please try again.", null, cause);
    }

    private async Task<int> CompleteCustomerAsync(BillingCustomerLink link, Mx.Customer customer)
    {
        if (customer.Id is not int id)
        {
            await ReleaseQuietlyAsync(link);
            _logger.LogError("Maxio returned customer {CustomerReference} without an id.", link.Reference);
            throw new BillingException(BillingErrorKind.ProviderFailure, "The billing service returned an unexpected response.");
        }

        if (!await _claims.TryCompleteAsync(link, id, UtcNow, CancellationToken.None))
        {
            _logger.LogWarning("Customer claim {CustomerReference} changed while being completed; another request took it over.", link.Reference);
        }

        return id;
    }

    private async Task<Mx.Customer?> FindCustomerAsync(string customerReference, CancellationToken token)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = customerReference },
                cancellationToken: token);
            return response.Customer;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // only a 404 means "absent"; every other failure propagates
        }
    }

    private async Task<Mx.Customer?> TryFindCustomerForSettleAsync(string customerReference)
    {
        try
        {
            using var settle = new CancellationTokenSource(SettleBudget);
            return await FindCustomerAsync(customerReference, settle.Token);
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Re-reading Maxio customer {CustomerReference} failed.", customerReference);
            return null;
        }
    }

    private static IReadOnlyList<string> DescribeCustomerErrors(Mx.CustomerErrorResponse1 body)
    {
        if (body.Errors is null)
        {
            return Array.Empty<string>();
        }

        if (body.Errors.TryGetListOfString(out var list))
        {
            return list;
        }

        if (body.Errors.TryGetCustomerError(out var error) && !string.IsNullOrWhiteSpace(error.Customer))
        {
            return new[] { error.Customer };
        }

        return Array.Empty<string>(); // unknown shape: nothing caller-safe to show
    }

    // ------------------------------------------------------------------ subscription: find-or-create

    private async Task<SubscribeResult> EnsureSubscriptionAsync(string customerReference, int customerId, SubscriptionPlan plan, CollectionMethod collectionMethod, CancellationToken token, CancellationToken callerToken)
    {
        var reference = MaxioReferences.ForSubscription(customerReference, plan.Handle);
        var now = UtcNow;
        var attempt = await _claims.TryClaimAsync(
            new SubscriptionEnrollment(reference, customerReference, plan.Handle, now, ClaimLease), now, ClaimLease, token);
        var enrollment = attempt.Claim;

        if (!attempt.Acquired)
        {
            if (enrollment.Status != BillingClaimStatus.Completed)
            {
                throw InProgress();
            }

            var current = await FindSubscriptionAsync(reference, token);
            if (current is null)
            {
                _logger.LogWarning("Subscription {SubscriptionReference} is recorded locally but Maxio has no subscription with that reference.", reference);
            }

            return new SubscribeResult(SubscribeOutcome.AlreadyExists, reference, current is null ? null : ToDetails(current));
        }

        // We hold the claim. Look before writing (covers a restart, a lost claim row, or a takeover of an abandoned claim).
        Mx.Subscription? existing;
        try
        {
            existing = await FindSubscriptionAsync(reference, token);
        }
        catch
        {
            await ReleaseQuietlyAsync(enrollment);
            throw;
        }

        if (existing is not null)
        {
            if (!BelongsTo(existing, customerId))
            {
                await ReleaseQuietlyAsync(enrollment);
                _logger.LogError("Maxio subscription with reference {SubscriptionReference} belongs to another customer.", reference);
                throw new BillingException(BillingErrorKind.ProviderFailure, "The subscription could not be created.");
            }

            await CompleteSubscriptionAsync(enrollment, existing);
            return new SubscribeResult(SubscribeOutcome.AlreadyExists, reference, ToDetails(existing));
        }

        return await CreateSubscriptionAsync(enrollment, customerId, plan, collectionMethod, token, callerToken);
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(SubscriptionEnrollment enrollment, int customerId, SubscriptionPlan plan, CollectionMethod collectionMethod, CancellationToken token, CancellationToken callerToken)
    {
        var request = new CreateSubscriptionOperationRequest
        {
            Body = new Mx.CreateSubscriptionRequest
            {
                Subscription = new Mx.CreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customerId,
                    Reference = enrollment.Reference,
                    // No payment method is captured at signup: bill by invoice (remittance/invoice per the site's architecture).
                    PaymentCollectionMethod = collectionMethod
                }
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(request, cancellationToken: token);
            var created = response.Subscription;
            if (created?.Id is null)
            {
                _logger.LogWarning("Maxio accepted subscription {SubscriptionReference} but returned no subscription; reconciling by reference.", enrollment.Reference);
                return await SettleSubscriptionAfterAmbiguousCreateAsync(enrollment, customerId);
            }

            await CompleteSubscriptionAsync(enrollment, created);
            _logger.LogInformation(
                "Created Maxio subscription {MaxioSubscriptionId} ({PlanHandle}, state {State}) for customer {MaxioCustomerId}, reference {SubscriptionReference}.",
                created.Id, plan.Handle, created.State?.Value, customerId, enrollment.Reference);
            return new SubscribeResult(SubscribeOutcome.Created, enrollment.Reference, ToDetails(created));
        }
        catch (ApiException<CreateSubscriptionError> ex) when (IsValidationRejection(ex.StatusCode))
        {
            var messages = ex.Error.Match(
                onErrorListResponse1: body => body.Errors ?? (IReadOnlyList<string>)Array.Empty<string>(),
                otherwise: _ => Array.Empty<string>());
            _logger.LogWarning(
                "Maxio rejected subscription {SubscriptionReference} to {PlanHandle} (HTTP {StatusCode}): {Errors}.",
                enrollment.Reference, plan.Handle, (int)ex.StatusCode, string.Join("; ", messages));
            if (messages.Any(m => m.Contains("collection", StringComparison.OrdinalIgnoreCase)))
            {
                _site.Invalidate(); // the cached architecture may be stale; re-read it on the next attempt
                _logger.LogError("Maxio refused payment collection method {CollectionMethod}; the site's architecture will be re-read.", collectionMethod.Value);
            }
            else if (messages.Any(m => m.Contains("payment method", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogError(
                    "Maxio requires a payment method for plan {PlanHandle} even with collection method {CollectionMethod}: check the product's configuration.",
                    plan.Handle, collectionMethod.Value);
            }

            // If the rejection is a duplicate reference, the subscription exists: adopt it.
            var adopted = await TryFindSubscriptionForSettleAsync(enrollment.Reference);
            if (adopted is not null && BelongsTo(adopted, customerId))
            {
                await CompleteSubscriptionAsync(enrollment, adopted);
                return new SubscribeResult(SubscribeOutcome.AlreadyExists, enrollment.Reference, ToDetails(adopted));
            }

            await ReleaseQuietlyAsync(enrollment);
            throw new BillingException(BillingErrorKind.Rejected, "The billing service rejected the subscription.", messages, ex);
        }
        catch (Exception ex) when (IsUnknownOutcome(ex, callerToken))
        {
            _logger.LogError(ex, "Creating Maxio subscription {SubscriptionReference} has an unknown outcome; reconciling by reference.", enrollment.Reference);
            return await SettleSubscriptionAfterAmbiguousCreateAsync(enrollment, customerId);
        }
        catch (SdkException ex)
        {
            await ReleaseQuietlyAsync(enrollment);
            throw Translate("create subscription", ex);
        }
    }

    /// <summary>
    /// Settles a subscription create whose outcome is unknown: find it by reference, falling back to the
    /// customer's subscription list. If it cannot be found, the claim stays pending (with a short cool-down)
    /// and the caller is told the outcome is pending; the next POST or GET re-reads before anything is resent.
    /// </summary>
    private async Task<SubscribeResult> SettleSubscriptionAfterAmbiguousCreateAsync(SubscriptionEnrollment enrollment, int customerId)
    {
        using var settle = new CancellationTokenSource(SettleBudget);
        Mx.Subscription? found = null;
        try
        {
            found = await FindSubscriptionAsync(enrollment.Reference, settle.Token);
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException or BillingException)
        {
            _logger.LogWarning(ex, "Looking up subscription {SubscriptionReference} failed; falling back to the customer's subscription list.", enrollment.Reference);
            try
            {
                var all = await ListSubscriptionsAsync(customerId, settle.Token);
                found = all.FirstOrDefault(s => string.Equals(s.Reference, enrollment.Reference, StringComparison.Ordinal));
            }
            catch (Exception listEx) when (listEx is SdkException or OperationCanceledException)
            {
                _logger.LogError(listEx, "Listing subscriptions for Maxio customer {MaxioCustomerId} failed while settling {SubscriptionReference}.", customerId, enrollment.Reference);
            }
        }

        if (found?.Id is not null && BelongsTo(found, customerId))
        {
            await CompleteSubscriptionAsync(enrollment, found);
            _logger.LogInformation("Reconciled Maxio subscription {MaxioSubscriptionId} for reference {SubscriptionReference}.", found.Id, enrollment.Reference);
            return new SubscribeResult(SubscribeOutcome.Created, enrollment.Reference, ToDetails(found));
        }

        await MarkUnknownAsync(enrollment);
        _logger.LogWarning("Subscription {SubscriptionReference} could not be confirmed; left pending for reconciliation.", enrollment.Reference);
        return new SubscribeResult(SubscribeOutcome.Pending, enrollment.Reference, null);
    }

    private async Task CompleteSubscriptionAsync(SubscriptionEnrollment enrollment, Mx.Subscription subscription)
    {
        if (subscription.Id is not int id)
        {
            return;
        }

        if (!await _claims.TryCompleteAsync(enrollment, id, UtcNow, CancellationToken.None))
        {
            _logger.LogWarning("Subscription claim {SubscriptionReference} changed while being completed; another request took it over.", enrollment.Reference);
        }
    }

    /// <summary>
    /// Settles pending enrollments against the customer's full subscription list (the sweep for unknown
    /// outcomes): found → completed; absent and the claim abandoned → released so a retry can create it.
    /// </summary>
    private async Task SettlePendingEnrollmentsAsync(string customerReference, IReadOnlyList<Mx.Subscription> subscriptions, CancellationToken token)
    {
        var pending = await _claims.ListPendingEnrollmentsAsync(customerReference, token);
        foreach (var enrollment in pending)
        {
            var match = subscriptions.FirstOrDefault(s => string.Equals(s.Reference, enrollment.Reference, StringComparison.Ordinal));
            if (match?.Id is int id)
            {
                await _claims.TryCompleteAsync(enrollment, id, UtcNow, token);
                _logger.LogInformation("Reconciled pending subscription {SubscriptionReference} as {MaxioSubscriptionId}.", enrollment.Reference, id);
            }
            else if (enrollment.IsLeaseExpired(UtcNow))
            {
                await _claims.ReleaseAsync(enrollment, token);
                _logger.LogInformation("Released abandoned subscription claim {SubscriptionReference}: Maxio has no such subscription.", enrollment.Reference);
            }
        }
    }

    private async Task<Mx.Subscription?> FindSubscriptionAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: token);
            return response.Subscription
                ?? throw new BillingException(BillingErrorKind.ProviderFailure, "The billing service returned an unexpected response.");
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Mx.Subscription?> TryFindSubscriptionForSettleAsync(string reference)
    {
        try
        {
            using var settle = new CancellationTokenSource(SettleBudget);
            return await FindSubscriptionAsync(reference, settle.Token);
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException or BillingException)
        {
            _logger.LogWarning(ex, "Re-reading Maxio subscription {SubscriptionReference} failed.", reference);
            return null;
        }
    }

    private async Task<IReadOnlyList<Mx.Subscription>> ListSubscriptionsAsync(int customerId, CancellationToken token)
    {
        var items = await _client.Customers.ListCustomerSubscriptions(
            new ListCustomerSubscriptionsRequest { CustomerId = customerId },
            cancellationToken: token);
        return items
            .Select(i => i?.Subscription)
            .Where(s => s?.Id is not null)
            .Select(s => s!)
            .ToList();
    }

    // ------------------------------------------------------------------ helpers

    private static bool BelongsTo(Mx.Subscription subscription, int customerId)
        => subscription.Customer?.Id is null || subscription.Customer.Id == customerId;

    private static SubscriptionDetails ToDetails(Mx.Subscription s) => new(
        s.Id ?? 0,
        s.Reference,
        s.Product?.Handle,
        s.Product?.Name,
        s.ProductPriceInCents ?? s.Product?.PriceInCents,
        s.Product?.Interval,
        s.Product?.IntervalUnit?.Value,
        s.State?.Value,
        s.CurrentPeriodEndsAt,
        s.NextAssessmentAt,
        s.CreatedAt);

    private static int ToMaxioId(long id) => checked((int)id);

    private static CancellationTokenSource StartBudget(CancellationToken callerToken, TimeSpan budget)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(budget);
        return cts;
    }

    private static BillingException InProgress()
        => new(BillingErrorKind.InProgress, "An identical request is already in progress. Check your subscriptions in a moment.");

    /// <summary>A 4xx that means the provider refused the request on its merits (not auth, throttling or a timeout).</summary>
    private static bool IsValidationRejection(HttpStatusCode status)
        => (int)status is >= 400 and < 500 and not 401 and not 403 and not 408 and not 429;

    /// <summary>
    /// True when a write may have landed: no usable response (connection drop, SDK timeout, our own budget
    /// expiring), a 5xx/408, or a 2xx whose body could not be read.
    /// </summary>
    private static bool IsUnknownOutcome(Exception ex, CancellationToken callerToken) => ex switch
    {
        SdkConnectionException => true,
        ApiException api => (int)api.StatusCode >= 500 || api.StatusCode == HttpStatusCode.RequestTimeout || (int)api.StatusCode < 300,
        OperationCanceledException => !callerToken.IsCancellationRequested,
        _ => false
    };

    private async Task ReleaseQuietlyAsync(BillingClaim claim)
    {
        try
        {
            switch (claim)
            {
                case BillingCustomerLink link:
                    await _claims.ReleaseAsync(link, CancellationToken.None);
                    break;
                case SubscriptionEnrollment enrollment:
                    await _claims.ReleaseAsync(enrollment, CancellationToken.None);
                    break;
            }
        }
        catch (Exception ex)
        {
            // The lease still expires, so a stuck claim is recoverable; do not mask the original failure.
            _logger.LogError(ex, "Releasing billing claim {Reference} failed.", claim.Reference);
        }
    }

    private async Task MarkUnknownAsync<T>(T claim) where T : BillingClaim
    {
        try
        {
            await _claims.TryRenewAsync(claim, UtcNow, UnknownOutcomeCoolDown, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recording the unknown outcome of billing claim {Reference} failed.", claim.Reference);
        }
    }

    private async Task<T> RunAsync<T>(string operation, Func<Task<T>> body, CancellationToken callerToken)
    {
        try
        {
            return await body();
        }
        catch (BillingException)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (!callerToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Maxio call budget of {Budget} exceeded during {Operation}.", RequestBudget, operation);
            throw new BillingException(BillingErrorKind.Unavailable, "The billing service did not respond in time. Please try again.", null, ex);
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex);
        }
    }

    /// <summary>One mapping from SDK failure to caller-facing failure, used for every operation.</summary>
    private BillingException Translate(string operation, SdkException ex)
    {
        switch (ex)
        {
            case ResponseDeserializationException rde when (int)rde.StatusCode < 300:
                _logger.LogError(ex, "Maxio returned an unreadable {StatusCode} response during {Operation}.", (int)rde.StatusCode, operation);
                return new BillingException(BillingErrorKind.ProviderFailure, "The billing service returned an unexpected response.", null, ex);

            case ApiException api when api.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                _logger.LogError(ex, "Maxio refused our credentials (HTTP {StatusCode}) during {Operation}. Check Maxio:ApiKey and Maxio:Subdomain.", (int)api.StatusCode, operation);
                return new BillingException(BillingErrorKind.ProviderFailure, "The billing service is not available.", null, ex);

            case ApiException api when api.StatusCode == HttpStatusCode.TooManyRequests:
                _logger.LogWarning(ex, "Maxio rate limit hit during {Operation}.", operation);
                return new BillingException(BillingErrorKind.Unavailable, "The billing service is busy. Please try again shortly.", null, ex);

            case ApiException api when (int)api.StatusCode >= 500 || api.StatusCode == HttpStatusCode.RequestTimeout:
                _logger.LogError(ex, "Maxio failed with HTTP {StatusCode} during {Operation}.", (int)api.StatusCode, operation);
                return new BillingException(BillingErrorKind.Unavailable, "The billing service is temporarily unavailable. Please try again.", null, ex);

            case ApiException api:
                _logger.LogWarning(ex, "Maxio answered HTTP {StatusCode} during {Operation}.", (int)api.StatusCode, operation);
                return new BillingException(BillingErrorKind.ProviderFailure, "The billing service could not process the request.", null, ex);

            case SdkConnectionException:
                _logger.LogError(ex, "Could not reach Maxio during {Operation}.", operation);
                return new BillingException(BillingErrorKind.Unavailable, "The billing service is temporarily unavailable. Please try again.", null, ex);

            case AuthSchemeException:
                _logger.LogError(ex, "Maxio credentials are not configured (during {Operation}).", operation);
                return new BillingException(BillingErrorKind.ProviderFailure, "The billing service is not available.", null, ex);

            default:
                _logger.LogError(ex, "Unexpected Maxio SDK failure during {Operation}.", operation);
                return new BillingException(BillingErrorKind.ProviderFailure, "The billing service is not available.", null, ex);
        }
    }
}
