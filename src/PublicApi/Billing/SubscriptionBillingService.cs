using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The one class that fronts the Maxio Advanced Billing SDK for the subscription capability.
/// <list type="bullet">
/// <item>Every public method runs under one total time budget linked to the caller's cancellation.</item>
/// <item>Each write (create customer, create subscription) is preceded by a claim row in <see cref="CatalogContext"/>
/// whose primary key is the deterministic reference sent to Maxio, so a second caller is refused by the store.</item>
/// <item>A write whose outcome is unknown (connection failure, timeout, 5xx, unreadable 2xx) is settled by re-reading
/// Maxio by that reference before anything is reported; what cannot be settled is recorded as Unknown and settled
/// by the next request for the same key.</item>
/// </list>
/// </summary>
public sealed class SubscriptionBillingService
{
    /// <summary>Maxio's maximum page size for product listings.</summary>
    public const int PlanPageSize = 200;

    /// <summary>Hard cap on product pages walked per listing (1,000 products).</summary>
    public const int MaxPlanPages = 5;

    /// <summary>Total budget for one inbound request, across every SDK call it makes.</summary>
    public static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(30);

    /// <summary>Budget for re-reading Maxio after a write whose outcome is unknown (independent of the request budget).</summary>
    public static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(15);

    /// <summary>A Pending claim older than this was abandoned by its owner and may be taken over.</summary>
    public static readonly TimeSpan MaxPendingAge = TimeSpan.FromMinutes(2);

    private const string CustomerLastName = "Customer";

    private readonly MaxioAdvancedBillingClient _client;
    private readonly SiteCollectionMethodCache _siteCollectionMethod;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        SiteCollectionMethodCache siteCollectionMethod,
        CatalogContext db,
        IOptions<MaxioSettings> settings,
        TimeProvider time,
        ILogger<SubscriptionBillingService> logger)
    {
        _client = client;
        _siteCollectionMethod = siteCollectionMethod;
        _db = db;
        _settings = settings.Value;
        _time = time;
        _logger = logger;
    }

    private DateTimeOffset Now => _time.GetUtcNow();

    // ------------------------------------------------------------------ public surface

    /// <summary>Lists the plans offered for subscription (backs GET /api/subscription-plans).</summary>
    public async Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken)
    {
        using var budget = CreateBudget(cancellationToken);
        try
        {
            return await ListPlansCoreAsync(budget.Token);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw BillingException.Timeout();
        }
    }

    /// <summary>
    /// Subscribes the caller to <paramref name="planHandle"/> (backs POST /api/subscriptions).
    /// One subscription per (user, plan): a repeat call returns the existing subscription.
    /// </summary>
    public async Task<SubscribeResult> SubscribeAsync(BillingCaller caller, string? planHandle, CancellationToken cancellationToken)
    {
        if (caller is null || string.IsNullOrWhiteSpace(caller.UserName)) throw BillingException.Unauthenticated();
        if (string.IsNullOrWhiteSpace(planHandle)) throw BillingException.InvalidRequest("planHandle is required.");
        planHandle = planHandle.Trim();

        using var budget = CreateBudget(cancellationToken);
        var token = budget.Token;
        try
        {
            // Invariant: the handle must be one this application offers (same member that backs GET /api/subscription-plans).
            // Checked before any claim and before any Maxio write.
            var catalog = await ListPlansCoreAsync(token);
            var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
            if (plan is null)
            {
                if (catalog.IsPartial)
                {
                    throw BillingException.ProviderUnavailable("The plan list could not be read completely, so the plan could not be verified. Retry shortly.");
                }

                throw BillingException.InvalidRequest("The requested plan is not offered. Use a handle returned by GET /api/subscription-plans.");
            }

            // Invariant: payment_collection_method must be valid for the site's invoicing architecture. Resolved
            // (process-cached) before any claim and any Maxio write; a failed or indeterminate read stops here.
            var collectionMethod = await _siteCollectionMethod.GetOrResolveAsync(ReadSiteCollectionMethodAsync, token);

            var customerReference = CustomerReference.ForUser(caller.UserName);
            var customerId = await EnsureCustomerAsync(caller, customerReference, token);
            return await EnrollAsync(customerReference, customerId, plan, collectionMethod, token);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw BillingException.Timeout();
        }
    }

    /// <summary>Lists the caller's subscriptions (backs GET /api/my-subscriptions) and settles any Unknown claims it can.</summary>
    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(BillingCaller caller, CancellationToken cancellationToken)
    {
        if (caller is null || string.IsNullOrWhiteSpace(caller.UserName)) throw BillingException.Unauthenticated();

        using var budget = CreateBudget(cancellationToken);
        var token = budget.Token;
        try
        {
            var customerReference = CustomerReference.ForUser(caller.UserName);
            var customerId = await ResolveCustomerIdForReadAsync(customerReference, token);
            if (customerId is null)
            {
                return Array.Empty<SubscriptionDto>();
            }

            // Invariant: CustomerId is the caller's own stored/looked-up Maxio id, never request input.
            var subscriptions = await ListCustomerSubscriptionsCoreAsync(customerId.Value, token);
            var enrollments = await _db.SubscriptionEnrollments
                .Where(e => e.CustomerReference == customerReference)
                .ToListAsync(token);

            await SettleEnrollmentsAsync(enrollments, subscriptions);

            var result = subscriptions
                .Select(s => Map(s, plan: null, fallbackPlanHandle: FindEnrollment(enrollments, s)?.PlanHandle))
                .ToList();

            // Subscribes whose outcome is still unconfirmed are shown as such, not hidden.
            foreach (var pending in enrollments.Where(e => e.State != BillingClaimState.Completed
                         && !subscriptions.Any(s => string.Equals(s.Reference, e.Key, StringComparison.Ordinal))))
            {
                result.Add(new SubscriptionDto { PlanHandle = pending.PlanHandle, State = SubscriptionDto.UnknownState, CreatedAt = pending.CreatedAt });
            }

            return result;
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw BillingException.Timeout();
        }
    }

    // ------------------------------------------------------------------ plans

    private async Task<PlanCatalog> ListPlansCoreAsync(CancellationToken token)
    {
        var plans = new List<SubscriptionPlanDto>();
        var complete = false;

        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse> items;
            try
            {
                items = await _client.ProductFamilies.ListProductsForProductFamily(
                    new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + _settings.ProductFamilyHandle!.Trim(),
                        Page = page,
                        PerPage = PlanPageSize
                    },
                    cancellationToken: token);
            }
            catch (ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    // 404: the configured product family does not exist on this site. A deployment fault, not the caller's.
                    _logger.LogError("Maxio product family lookup failed with {StatusCode}; check Maxio:ProductFamilyHandle and the site.", (int)ex.StatusCode);
                    throw BillingException.ProviderFailure("The subscription plan catalog is not available.");
                }

                var status = ex.Error.TryGetRawError(out RawError raw) ? raw.StatusCode : ex.StatusCode;
                throw FromProviderStatus(status, "ListProductsForProductFamily");
            }
            catch (SdkException ex)
            {
                throw TranslateSdkFailure(ex, "ListProductsForProductFamily");
            }

            foreach (var product in items.Select(i => i.Product))
            {
                // Offered = in the family, not archived, has a handle, and needs no payment method (this flow captures none).
                if (product is null || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null || product.RequireCreditCard == true)
                {
                    continue;
                }

                plans.Add(new SubscriptionPlanDto
                {
                    Handle = product.Handle,
                    Name = product.Name,
                    Description = product.Description,
                    Price = PriceDto.FromCents(product.PriceInCents, currency: null),
                    Interval = BillingIntervalDto.From(product.Interval, product.IntervalUnit?.Value)
                });
            }

            if (items.Count < PlanPageSize)
            {
                complete = true;
                break;
            }
        }

        if (!complete)
        {
            _logger.LogWarning("Maxio plan listing stopped at the {MaxPages}-page cap; the result is partial.", MaxPlanPages);
        }

        return new PlanCatalog(plans, IsPartial: !complete);
    }

    // ------------------------------------------------------------------ site invoicing architecture

    /// <summary>
    /// Reads the site and maps its invoicing architecture to the collection method CreateSubscription must send:
    /// Relationship Invoicing (true) is remittance, legacy Statements (false) is invoice. Never automatic: this
    /// flow captures no payment method. A null flag is indeterminate and fails closed; it is never read as false.
    /// Runs only when <see cref="SiteCollectionMethodCache"/> has nothing cached; a throw here is not cached.
    /// </summary>
    private async Task<MaxioAdvancedBilling.Models.Enums.CollectionMethod> ReadSiteCollectionMethodAsync(CancellationToken token)
    {
        MaxioAdvancedBilling.Models.SiteResponse response;
        try
        {
            response = await _client.Sites.ReadSite(cancellationToken: token);
        }
        catch (ApiException<RawError> ex)
        {
            throw FromProviderStatus(ex.StatusCode, "ReadSite");
        }
        catch (SdkException ex)
        {
            throw TranslateSdkFailure(ex, "ReadSite");
        }

        var relationshipInvoicing = response.Site?.RelationshipInvoicingEnabled;
        if (relationshipInvoicing is not bool enabled)
        {
            _logger.LogError("Maxio ReadSite returned no relationship_invoicing_enabled flag; the site's invoicing architecture is indeterminate, so no subscription is created.");
            throw BillingException.ProviderFailure("The billing provider's site configuration could not be determined. No subscription was created.");
        }

        var method = enabled
            ? MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
            : MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice;
        _logger.LogInformation(
            "Maxio site invoicing architecture resolved and cached: relationship_invoicing_enabled={RelationshipInvoicingEnabled}, payment_collection_method={CollectionMethod}.",
            enabled, method.Value);
        return method;
    }

    // ------------------------------------------------------------------ customer

    private async Task<int> EnsureCustomerAsync(BillingCaller caller, string reference, CancellationToken token)
    {
        var existing = await FindClaimAsync<BillingCustomerLink>(reference, token);
        if (existing is { State: BillingClaimState.Completed, ProviderCustomerId: int linkedId })
        {
            return linkedId;
        }

        // DUPLICATE CLAIM: insert the Pending link (PK = customer reference) before any Maxio call.
        var (link, tookOver) = await ClaimAsync(existing, () => new BillingCustomerLink(reference, Now), token);
        if (link is { State: BillingClaimState.Completed, ProviderCustomerId: int winnerId })
        {
            return winnerId;
        }

        // Adopt a customer that already exists for this reference (restart, lost in-memory store, earlier unknown outcome).
        MaxioAdvancedBilling.Models.Customer? found;
        try
        {
            found = await ReadCustomerByReferenceAsync(reference, token);
        }
        catch
        {
            await RestoreClaimAsync(link, tookOver);
            throw;
        }

        if (found?.Id is int foundId)
        {
            link.MarkLinked(foundId, Now);
            await TrySaveAsync(link, CancellationToken.None);
            _logger.LogInformation("Adopted existing Maxio customer {CustomerId} for reference {CustomerReference}.", foundId, reference);
            return foundId;
        }

        return await CreateCustomerAsync(caller, reference, link, token);
    }

    private async Task<int> CreateCustomerAsync(BillingCaller caller, string reference, BillingCustomerLink link, CancellationToken token)
    {
        var email = !string.IsNullOrWhiteSpace(caller.Email) ? caller.Email!.Trim() : caller.UserName.Trim();
        var at = email.IndexOf('@');
        var firstName = at > 0 ? email[..at] : email;

        MaxioAdvancedBilling.Models.CustomerResponse? response = null;
        string unknownReason;
        try
        {
            response = await _client.Customers.CreateCustomer(
                new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
                {
                    Body = new MaxioAdvancedBilling.Models.CreateCustomerRequest
                    {
                        Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = CustomerLastName,
                            Email = email,
                            Reference = reference
                        }
                    }
                },
                cancellationToken: token);

            if (response.Customer?.Id is int createdId)
            {
                link.MarkLinked(createdId, Now);
                await TrySaveAsync(link, CancellationToken.None);
                _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.", createdId, reference);
                return createdId;
            }

            unknownReason = "2xx without a customer id";
        }
        catch (ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var invalid))
            {
                // 422. The reference must be unique, so a duplicate may be why: re-read before reporting a failure.
                var errors = ErrorsOf(invalid);
                var adopted = await TrySettleCustomerAsync(reference);
                if (adopted is int adoptedId)
                {
                    link.MarkLinked(adoptedId, Now);
                    await TrySaveAsync(link, CancellationToken.None);
                    _logger.LogInformation("CreateCustomer answered 422; adopted existing Maxio customer {CustomerId} for reference {CustomerReference}.", adoptedId, reference);
                    return adoptedId;
                }

                await DeleteClaimAsync(link);
                _logger.LogError("Maxio rejected CreateCustomer for reference {CustomerReference} with 422: {Errors}", reference, string.Join("; ", errors));
                throw BillingException.Rejected("The billing provider rejected the customer record.", errors);
            }

            var status = ex.Error.TryGetRawError(out RawError raw) ? raw.StatusCode : ex.StatusCode;
            if ((int)status < 500)
            {
                await DeleteClaimAsync(link);
                throw FromProviderStatus(status, "CreateCustomer");
            }

            unknownReason = $"HTTP {(int)status}";
        }
        catch (ResponseDeserializationException ex) when (!IsUnknownStatus(ex.StatusCode))
        {
            // The provider refused the call; only the error detail was unreadable.
            await DeleteClaimAsync(link);
            throw FromProviderStatus(ex.StatusCode, "CreateCustomer");
        }
        catch (ResponseDeserializationException ex)
        {
            unknownReason = $"unreadable HTTP {(int)ex.StatusCode} body";
        }
        catch (AuthSchemeException ex)
        {
            await DeleteClaimAsync(link);
            throw TranslateSdkFailure(ex, "CreateCustomer");
        }
        catch (SdkConnectionException ex)
        {
            // Includes SdkTimeoutException. The POST may have landed.
            unknownReason = ex is SdkTimeoutException ? "timeout" : "connection failure";
        }
        catch (OperationCanceledException)
        {
            unknownReason = "request budget or caller cancellation";
        }

        // UNKNOWN OUTCOME: settle by re-reading the customer by the reference we sent.
        _logger.LogWarning("CreateCustomer outcome unknown ({Reason}) for reference {CustomerReference}; re-reading by reference.", unknownReason, reference);
        var settled = await TrySettleCustomerAsync(reference);
        if (settled is int settledId)
        {
            link.MarkLinked(settledId, Now);
            await TrySaveAsync(link, CancellationToken.None);
            _logger.LogInformation("Settled CreateCustomer: Maxio customer {CustomerId} exists for reference {CustomerReference}.", settledId, reference);
            return settledId;
        }

        link.MarkUnknown(Now);
        await TrySaveAsync(link, CancellationToken.None);
        _logger.LogWarning("CreateCustomer for reference {CustomerReference} is still unsettled; recorded as Unknown for the next request.", reference);
        throw BillingException.ProviderUnavailable("Your billing account is still being set up. Retry shortly.");
    }

    /// <summary>Re-reads the customer by reference under its own budget. Returns null when absent or when the read fails.</summary>
    private async Task<int?> TrySettleCustomerAsync(string reference)
    {
        using var settle = new CancellationTokenSource(SettleBudget);
        try
        {
            var customer = await ReadCustomerByReferenceAsync(reference, settle.Token);
            return customer?.Id;
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            _logger.LogWarning("Re-reading Maxio customer for reference {CustomerReference} failed ({Failure}).", reference, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>Returns the customer for <paramref name="reference"/>, or null when Maxio answers 404. Any other failure throws.</summary>
    private async Task<MaxioAdvancedBilling.Models.Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: token);

            if (response.Customer?.Id is null)
            {
                _logger.LogError("Maxio ReadCustomerByReference returned a customer without an id.");
                throw BillingException.ProviderFailure("The billing provider returned an incomplete customer record.");
            }

            return response.Customer;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // the only status that means "no customer with that reference"
        }
        catch (ApiException<RawError> ex)
        {
            throw FromProviderStatus(ex.StatusCode, "ReadCustomerByReference");
        }
        catch (SdkException ex)
        {
            // Includes ResponseDeserializationException: an unreadable answer is never treated as "absent".
            throw TranslateSdkFailure(ex, "ReadCustomerByReference");
        }
    }

    /// <summary>Customer id for a read-only call: the stored link, else a lookup by reference (which also settles an unknown link).</summary>
    private async Task<int?> ResolveCustomerIdForReadAsync(string reference, CancellationToken token)
    {
        var link = await FindClaimAsync<BillingCustomerLink>(reference, token);
        if (link is { State: BillingClaimState.Completed, ProviderCustomerId: int linkedId })
        {
            return linkedId;
        }

        var customer = await ReadCustomerByReferenceAsync(reference, token);
        if (customer?.Id is not int customerId)
        {
            return null;
        }

        if (link is null)
        {
            var created = new BillingCustomerLink(reference, Now);
            created.MarkLinked(customerId, Now);
            _db.BillingCustomerLinks.Add(created);
            await TrySaveAsync(created, CancellationToken.None);
        }
        else if (link.State == BillingClaimState.Unknown || link.IsStalePending(Now, MaxPendingAge))
        {
            link.MarkLinked(customerId, Now);
            await TrySaveAsync(link, CancellationToken.None);
            _logger.LogInformation("Settled customer link for reference {CustomerReference} to Maxio customer {CustomerId}.", reference, customerId);
        }

        return customerId;
    }

    // ------------------------------------------------------------------ subscription

    private async Task<SubscribeResult> EnrollAsync(string customerReference, int customerId, SubscriptionPlanDto plan,
        MaxioAdvancedBilling.Models.Enums.CollectionMethod collectionMethod, CancellationToken token)
    {
        var key = SubscriptionEnrollment.BuildKey(customerReference, plan.Handle);
        var existing = await FindClaimAsync<SubscriptionEnrollment>(key, token);
        if (existing is { State: BillingClaimState.Completed })
        {
            return new SubscribeResult(SubscribeOutcome.Existing, await ReadEnrolledAsync(existing, customerId, plan, token));
        }

        // DUPLICATE CLAIM: insert the Pending enrollment (PK = subscription reference) before any Maxio write.
        var (enrollment, tookOver) = await ClaimAsync(existing, () => new SubscriptionEnrollment(customerReference, plan.Handle, Now), token);
        if (enrollment.State == BillingClaimState.Completed)
        {
            return new SubscribeResult(SubscribeOutcome.Existing, await ReadEnrolledAsync(enrollment, customerId, plan, token));
        }

        // Adopt a subscription that already carries this reference (restart, lost in-memory store, earlier unknown outcome).
        IReadOnlyList<MaxioAdvancedBilling.Models.Subscription> current;
        try
        {
            current = await ListCustomerSubscriptionsCoreAsync(customerId, token);
        }
        catch
        {
            await RestoreClaimAsync(enrollment, tookOver);
            throw;
        }

        var match = FindByReference(current, key);
        if (match?.Id is int matchedId)
        {
            enrollment.MarkSubscribed(matchedId, Now);
            await TrySaveAsync(enrollment, CancellationToken.None);
            _logger.LogInformation("Adopted existing Maxio subscription {SubscriptionId} for {EnrollmentKey}.", matchedId, key);
            return new SubscribeResult(SubscribeOutcome.Existing, Map(match, plan));
        }

        return await CreateSubscriptionAsync(customerId, plan, collectionMethod, enrollment, token);
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(int customerId, SubscriptionPlanDto plan,
        MaxioAdvancedBilling.Models.Enums.CollectionMethod collectionMethod, SubscriptionEnrollment enrollment, CancellationToken token)
    {
        var key = enrollment.Key;
        string unknownReason;
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
                {
                    Body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                    {
                        Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            Reference = key,
                            // remittance (Relationship Invoicing) or invoice (legacy Statements), from the site read.
                            PaymentCollectionMethod = collectionMethod
                        }
                    }
                },
                cancellationToken: token);

            if (response.Subscription?.Id is int createdId)
            {
                enrollment.MarkSubscribed(createdId, Now);
                await TrySaveAsync(enrollment, CancellationToken.None);
                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for {EnrollmentKey}.", createdId, key);
                return new SubscribeResult(SubscribeOutcome.Created, Map(response.Subscription, plan));
            }

            unknownReason = "2xx without a subscription id";
        }
        catch (ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var invalid))
            {
                // 422 (validation, or a payment step this flow cannot complete): a definitive refusal.
                var errors = invalid.Errors ?? Array.Empty<string>();
                await DeleteClaimAsync(enrollment);
                _logger.LogError("Maxio rejected CreateSubscription for {EnrollmentKey} with 422: {Errors}", key, string.Join("; ", errors));
                throw BillingException.Rejected("The billing provider rejected the subscription.", errors);
            }

            var status = ex.Error.TryGetRawError(out RawError raw) ? raw.StatusCode : ex.StatusCode;
            if ((int)status < 500)
            {
                await DeleteClaimAsync(enrollment);
                throw FromProviderStatus(status, "CreateSubscription");
            }

            unknownReason = $"HTTP {(int)status}";
        }
        catch (ResponseDeserializationException ex) when (!IsUnknownStatus(ex.StatusCode))
        {
            await DeleteClaimAsync(enrollment);
            throw FromProviderStatus(ex.StatusCode, "CreateSubscription");
        }
        catch (ResponseDeserializationException ex)
        {
            unknownReason = $"unreadable HTTP {(int)ex.StatusCode} body";
        }
        catch (AuthSchemeException ex)
        {
            await DeleteClaimAsync(enrollment);
            throw TranslateSdkFailure(ex, "CreateSubscription");
        }
        catch (SdkConnectionException ex)
        {
            unknownReason = ex is SdkTimeoutException ? "timeout" : "connection failure";
        }
        catch (OperationCanceledException)
        {
            unknownReason = "request budget or caller cancellation";
        }

        // UNKNOWN OUTCOME: settle by listing the customer's subscriptions and matching the reference we sent.
        _logger.LogWarning("CreateSubscription outcome unknown ({Reason}) for {EnrollmentKey}; re-reading by reference.", unknownReason, key);
        var settled = await TrySettleSubscriptionAsync(customerId, key);
        if (settled?.Id is int settledId)
        {
            enrollment.MarkSubscribed(settledId, Now);
            await TrySaveAsync(enrollment, CancellationToken.None);
            _logger.LogInformation("Settled CreateSubscription: Maxio subscription {SubscriptionId} exists for {EnrollmentKey}.", settledId, key);
            return new SubscribeResult(SubscribeOutcome.Created, Map(settled, plan));
        }

        enrollment.MarkUnknown(Now);
        await TrySaveAsync(enrollment, CancellationToken.None);
        _logger.LogWarning("CreateSubscription for {EnrollmentKey} is still unsettled; recorded as Unknown for the next request.", key);
        return new SubscribeResult(SubscribeOutcome.Unknown, new SubscriptionDto
        {
            PlanHandle = plan.Handle,
            PlanName = plan.Name,
            Price = plan.Price,
            Interval = plan.Interval,
            State = SubscriptionDto.UnknownState,
            CreatedAt = enrollment.CreatedAt
        });
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription?> TrySettleSubscriptionAsync(int customerId, string reference)
    {
        using var settle = new CancellationTokenSource(SettleBudget);
        try
        {
            var subscriptions = await ListCustomerSubscriptionsCoreAsync(customerId, settle.Token);
            return FindByReference(subscriptions, reference);
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            _logger.LogWarning("Re-reading Maxio subscriptions for {EnrollmentKey} failed ({Failure}).", reference, ex.GetType().Name);
            return null;
        }
    }

    private async Task<SubscriptionDto> ReadEnrolledAsync(SubscriptionEnrollment enrollment, int customerId, SubscriptionPlanDto plan, CancellationToken token)
    {
        var subscriptions = await ListCustomerSubscriptionsCoreAsync(customerId, token);
        var match = subscriptions.FirstOrDefault(s => s.Id is int id && id == enrollment.ProviderSubscriptionId)
                    ?? FindByReference(subscriptions, enrollment.Key);
        if (match is not null)
        {
            return Map(match, plan);
        }

        _logger.LogWarning("Recorded Maxio subscription {SubscriptionId} for {EnrollmentKey} was not found at Maxio.", enrollment.ProviderSubscriptionId, enrollment.Key);
        return new SubscriptionDto
        {
            SubscriptionId = enrollment.ProviderSubscriptionId,
            PlanHandle = plan.Handle,
            PlanName = plan.Name,
            Price = plan.Price,
            Interval = plan.Interval,
            State = SubscriptionDto.UnknownState,
            CreatedAt = enrollment.CreatedAt
        };
    }

    private async Task<IReadOnlyList<MaxioAdvancedBilling.Models.Subscription>> ListCustomerSubscriptionsCoreAsync(int customerId, CancellationToken token)
    {
        try
        {
            var items = await _client.Customers.ListCustomerSubscriptions(
                new MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: token);
            return items.Select(i => i.Subscription).OfType<MaxioAdvancedBilling.Models.Subscription>().ToList();
        }
        catch (ApiException<RawError> ex)
        {
            throw FromProviderStatus(ex.StatusCode, "ListCustomerSubscriptions");
        }
        catch (SdkException ex)
        {
            throw TranslateSdkFailure(ex, "ListCustomerSubscriptions");
        }
    }

    /// <summary>Settles Unknown (or abandoned Pending) enrollments that Maxio shows as created.</summary>
    private async Task SettleEnrollmentsAsync(IEnumerable<SubscriptionEnrollment> enrollments, IReadOnlyList<MaxioAdvancedBilling.Models.Subscription> subscriptions)
    {
        foreach (var enrollment in enrollments)
        {
            if (enrollment.State != BillingClaimState.Unknown && !enrollment.IsStalePending(Now, MaxPendingAge)) continue;
            var match = FindByReference(subscriptions, enrollment.Key);
            if (match?.Id is not int id) continue;

            enrollment.MarkSubscribed(id, Now);
            if (await TrySaveAsync(enrollment, CancellationToken.None))
            {
                _logger.LogInformation("Settled enrollment {EnrollmentKey} to Maxio subscription {SubscriptionId}.", enrollment.Key, id);
            }
        }
    }

    // ------------------------------------------------------------------ claims (the store refuses the second caller)

    private Task<T?> FindClaimAsync<T>(string key, CancellationToken token) where T : BillingClaim =>
        _db.Set<T>().SingleOrDefaultAsync(c => c.Key == key, token);

    /// <summary>
    /// Takes the claim for a write. A new claim is an INSERT keyed by the provider reference: a concurrent
    /// second insert is refused by the store and caught here. An Unknown or abandoned Pending claim is taken
    /// over with an optimistic-concurrency update, so only one caller wins it. A live Pending claim means
    /// another request is mid-flight: 409. A Completed claim is returned as-is for the caller to serve.
    /// </summary>
    private async Task<(T Claim, bool TookOver)> ClaimAsync<T>(T? existing, Func<T> createClaim, CancellationToken token) where T : BillingClaim
    {
        if (existing is null)
        {
            var claim = createClaim();
            _db.Set<T>().Add(claim);
            try
            {
                await _db.SaveChangesAsync(token);
                return (claim, false);
            }
            catch (Exception ex) when (IsStoreRefusal(ex))
            {
                // The store refused the insert. Confirm that is because the key is taken (another caller's claim).
                _db.Entry(claim).State = EntityState.Detached;
                existing = await FindClaimAsync<T>(claim.Key, CancellationToken.None);
                if (existing is null)
                {
                    throw;
                }

                _logger.LogInformation("Billing claim {ClaimKey} is already held ({State}); this request did not reach Maxio.", claim.Key, existing.State);
            }
        }

        if (existing.State == BillingClaimState.Completed)
        {
            return (existing, false);
        }

        if (existing.State == BillingClaimState.Pending && !existing.IsStalePending(Now, MaxPendingAge))
        {
            throw BillingException.InProgress();
        }

        existing.TakeOver(Now);
        try
        {
            await _db.SaveChangesAsync(token);
            return (existing, true);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.Entry(existing).State = EntityState.Detached;
            throw BillingException.InProgress();
        }
    }

    /// <summary>Releases a claim after a definitive provider refusal.</summary>
    private async Task DeleteClaimAsync(BillingClaim claim)
    {
        _db.Remove(claim);
        await TrySaveAsync(claim, CancellationToken.None);
    }

    /// <summary>Undoes a claim when the request fails before any write: a new claim is released; a taken-over one goes back to Unknown.</summary>
    private async Task RestoreClaimAsync(BillingClaim claim, bool tookOver)
    {
        if (tookOver)
        {
            claim.MarkUnknown(Now);
            await TrySaveAsync(claim, CancellationToken.None);
        }
        else
        {
            await DeleteClaimAsync(claim);
        }
    }

    private async Task<bool> TrySaveAsync(BillingClaim claim, CancellationToken token)
    {
        try
        {
            await _db.SaveChangesAsync(token);
            return true;
        }
        catch (Exception ex) when (IsStoreRefusal(ex))
        {
            // Another request changed the row first (concurrency token) or inserted it; it now owns the outcome.
            _logger.LogWarning("Billing claim {ClaimKey} was changed by another request ({Failure}); leaving it to that request.", claim.Key, ex.GetType().Name);
            _db.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// What the store raises when it refuses a write: DbUpdateException (SQL Server duplicate key, and any
    /// concurrency-token conflict) or ArgumentException (EF Core in-memory duplicate key; pinned by BillingClaimStoreTests).
    /// </summary>
    private static bool IsStoreRefusal(Exception ex) => ex is DbUpdateException or ArgumentException;

    private static CancellationTokenSource CreateBudget(CancellationToken cancellationToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(RequestBudget);
        return budget;
    }

    /// <summary>2xx (the write landed but its answer is unreadable) and 5xx (the provider may have acted) leave the outcome unknown.</summary>
    private static bool IsUnknownStatus(HttpStatusCode status) => (int)status is (>= 200 and < 300) or >= 500;

    private static MaxioAdvancedBilling.Models.Subscription? FindByReference(IEnumerable<MaxioAdvancedBilling.Models.Subscription> subscriptions, string reference) =>
        subscriptions.FirstOrDefault(s => string.Equals(s.Reference, reference, StringComparison.Ordinal));

    private static SubscriptionEnrollment? FindEnrollment(IEnumerable<SubscriptionEnrollment> enrollments, MaxioAdvancedBilling.Models.Subscription subscription) =>
        enrollments.FirstOrDefault(e => string.Equals(e.Key, subscription.Reference, StringComparison.Ordinal)
                                        || (e.ProviderSubscriptionId is int id && id == subscription.Id));

    private static IReadOnlyList<string> ErrorsOf(MaxioAdvancedBilling.Models.CustomerErrorResponse1 body)
    {
        if (body.Errors is null) return Array.Empty<string>();
        if (body.Errors.TryGetListOfString(out var list)) return list;
        if (body.Errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError.Customer))
        {
            return new[] { customerError.Customer! };
        }

        return Array.Empty<string>();
    }

    internal static SubscriptionDto Map(MaxioAdvancedBilling.Models.Subscription subscription, SubscriptionPlanDto? plan, string? fallbackPlanHandle = null)
    {
        // With Maxio's newer catalog, the embedded product may be null: fall back to the plan we validated against.
        var product = subscription.Product;
        var state = subscription.State?.Value;
        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id,
            PlanHandle = product?.Handle ?? plan?.Handle ?? fallbackPlanHandle,
            PlanName = product?.Name ?? plan?.Name,
            Price = PriceDto.FromCents(subscription.ProductPriceInCents ?? product?.PriceInCents ?? plan?.Price.AmountInCents, subscription.Currency),
            Interval = BillingIntervalDto.From(product?.Interval, product?.IntervalUnit?.Value) ?? plan?.Interval,
            State = string.IsNullOrWhiteSpace(state) ? SubscriptionDto.UnknownState : state,
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    private BillingException FromProviderStatus(HttpStatusCode status, string operation)
    {
        var code = (int)status;
        _logger.LogError("Maxio {Operation} failed with HTTP {StatusCode}.", operation, code);
        return code switch
        {
            401 or 403 => BillingException.ProviderFailure("The billing provider rejected this application's credentials."),
            404 => BillingException.ProviderFailure("A billing resource this application is configured to use was not found."),
            429 => BillingException.ProviderUnavailable("The billing provider is rate limiting requests. Retry shortly."),
            >= 500 => BillingException.ProviderFailure("The billing provider failed to process the request."),
            _ => BillingException.ProviderFailure("The billing provider refused the request.")
        };
    }

    private BillingException TranslateSdkFailure(SdkException ex, string operation)
    {
        switch (ex)
        {
            case SdkTimeoutException:
                _logger.LogWarning("Maxio {Operation} timed out.", operation);
                return BillingException.Timeout();
            case SdkConnectionException:
                _logger.LogWarning("Maxio {Operation} could not reach the provider.", operation);
                return BillingException.ProviderUnavailable("The billing provider could not be reached. Retry shortly.");
            case ResponseDeserializationException rde:
                _logger.LogError("Maxio {Operation} returned an unreadable HTTP {StatusCode} body.", operation, (int)rde.StatusCode);
                return BillingException.ProviderFailure("The billing provider returned a response that could not be processed.");
            case ApiException api:
                return FromProviderStatus(api.StatusCode, operation);
            case AuthSchemeException:
                _logger.LogError("Maxio {Operation}: the API credentials could not be applied.", operation);
                return BillingException.ProviderFailure("The billing provider credentials could not be applied.");
            default:
                _logger.LogError("Maxio {Operation} failed ({Failure}).", operation, ex.GetType().Name);
                return BillingException.ProviderFailure("The billing provider request failed.");
        }
    }
}
