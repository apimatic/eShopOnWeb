using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using MaxioAdvancedBilling.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing. Idempotent per (user, plan):
/// a deterministic claim row and deterministic provider references guarantee that a
/// double-click never creates a second customer or subscription, and that an ambiguous
/// transport failure is settled by re-reading the provider by reference.
/// </summary>
public sealed class MaxioBillingService : ISubscriptionBillingService
{
    private const string PlansCacheKey = "maxio:subscription-plans";
    private static readonly TimeSpan PlansCacheLifetime = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ReconcileDelay = TimeSpan.FromMilliseconds(250);
    private const int ReconcileAttempts = 3;
    private const int ConcurrentWaitAttempts = 20;
    private const int PlanPageSize = 200;
    private const int MaxPlanPages = 5;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly AppIdentityDbContext _identityDb;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly MaxioOptions _options;

    public MaxioBillingService(MaxioAdvancedBillingClient client,
        AppIdentityDbContext identityDb,
        IMemoryCache cache,
        ILogger<MaxioBillingService> logger,
        IOptions<MaxioOptions> options)
    {
        _client = client;
        _identityDb = identityDb;
        _cache = cache;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken)
    {
        return await BoundedAsync(async token =>
        {
            if (_cache.TryGetValue(PlansCacheKey, out IReadOnlyList<SubscriptionPlanInfo>? cachedPlans) &&
                cachedPlans is not null)
            {
                return cachedPlans;
            }

            var plans = await FetchPlansAsync(token);
            _cache.Set(PlansCacheKey, plans, PlansCacheLifetime);
            return plans;
        }, cancellationToken);
    }

    public async Task<SubscriptionInfo> SubscribeAsync(string eshopUserId, string email, string planHandle, CancellationToken cancellationToken)
    {
        return await BoundedAsync<SubscriptionInfo>(async token =>
        {
            if (string.IsNullOrWhiteSpace(planHandle))
            {
                throw new MaxioBillingException("A subscription plan handle is required.", (int)HttpStatusCode.BadRequest);
            }

            // A plan a caller may subscribe to is one this application offers — the set the
            // plans endpoint returned. Anything else is rejected before reaching the provider.
            var plans = await ListPlansAsync(token);
            if (!plans.Any(plan => string.Equals(plan.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("Subscribe rejected: plan handle {PlanHandle} is not one of the offered plans", planHandle);
                throw new MaxioBillingException($"Unknown subscription plan '{planHandle}'.", (int)HttpStatusCode.BadRequest);
            }

            var subscriptionReference = SubscriptionReference(eshopUserId, planHandle);

            var existingLink = await FindSubscriptionLinkAsync(eshopUserId, planHandle, token);
            if (existingLink?.MaxioSubscriptionId is int recordedId)
            {
                var recorded = await ReadSubscriptionOrNullAsync(recordedId, token);
                if (recorded?.Subscription is not null)
                {
                    return MapSubscription(recorded.Subscription, existing: true);
                }

                // The subscription no longer exists at the provider; clear the stale claim and re-subscribe.
                _identityDb.MaxioSubscriptionLinks.Remove(existingLink);
                await _identityDb.SaveChangesAsync(token);
            }

            var customerId = await EnsureCustomerAsync(eshopUserId, email, token);

            // Claim: the composite primary key refuses a concurrent double-subscribe before
            // either caller reaches the provider. A row still present from an earlier attempt
            // whose outcome never settled is reused as the claim instead of re-added.
            var claim = await FindSubscriptionLinkAsync(eshopUserId, planHandle, token);
            if (claim is null)
            {
                claim = new MaxioSubscriptionLink
                {
                    EShopUserId = eshopUserId,
                    PlanHandle = planHandle,
                    MaxioCustomerId = customerId,
                    MaxioReference = subscriptionReference,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                try
                {
                    _identityDb.MaxioSubscriptionLinks.Add(claim);
                    await _identityDb.SaveChangesAsync(token);
                }
                catch (DbUpdateException)
                {
                    _identityDb.ChangeTracker.Clear();
                    claim = await FindSubscriptionLinkAsync(eshopUserId, planHandle, token);
                }
                catch (ArgumentException)
                {
                    _identityDb.ChangeTracker.Clear();
                    claim = await FindSubscriptionLinkAsync(eshopUserId, planHandle, token);
                }

                if (claim is null)
                {
                    return await AwaitConcurrentSubscriptionAsync(eshopUserId, planHandle, subscriptionReference, token);
                }
            }

            try
            {
                var prior = await FindSubscriptionOrNullAsync(subscriptionReference, token);
                if (prior?.Subscription is not null)
                {
                    await RecordSubscriptionLinkAsync(claim, prior.Subscription, token);
                    _logger.LogInformation("Subscription {SubscriptionId} for plan {PlanHandle} already existed for user {UserId}",
                        prior.Subscription.Id, planHandle, eshopUserId);
                    return MapSubscription(prior.Subscription, existing: true);
                }

                var response = await CreateSubscriptionViaProviderAsync(planHandle, customerId, subscriptionReference, token);

                if (response.Subscription is null)
                {
                    throw new MaxioBillingException("The billing provider returned an empty subscription.", (int)HttpStatusCode.BadGateway);
                }

                await RecordSubscriptionLinkAsync(claim, response.Subscription, token);
                _logger.LogInformation("Created subscription {SubscriptionId} (plan {PlanHandle}, customer {CustomerId}) for user {UserId}",
                    response.Subscription.Id, planHandle, customerId, eshopUserId);
                return MapSubscription(response.Subscription, existing: false);
            }
            catch (MaxioBillingException ex) when (ex.StatusCode is not null)
            {
                // A refused signup created nothing — unless the provider rejected our
                // deterministic reference as a duplicate, which means a subscription with
                // that reference already exists (the claim store did not refuse the second
                // claim fast enough). Settle by reference before reporting a failure.
                if (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
                {
                    var raced = await FindSubscriptionOrNullAsync(subscriptionReference, token);
                    if (raced?.Subscription is not null)
                    {
                        await RecordSubscriptionLinkAsync(claim, raced.Subscription, token);
                        _logger.LogInformation(
                            "Subscription {SubscriptionId} for plan {PlanHandle} already held reference {Reference}; returning it (concurrent subscribe)",
                            raced.Subscription.Id, planHandle, subscriptionReference);
                        return MapSubscription(raced.Subscription, existing: true);
                    }
                }

                // The provider refused the call: release the claim so a corrected retry can proceed.
                await ReleaseSubscriptionClaimAsync(claim, token);
                throw;
            }
            catch (MaxioBillingException ex) when (ex.StatusCode is null)
            {
                // Transport failure: the subscription may still have been created — settle by reference.
                Subscription? settled = null;
                var settleFailed = false;
                try
                {
                    settled = await SettleSubscriptionByReferenceAsync(subscriptionReference, token);
                }
                catch (MaxioBillingException)
                {
                    settleFailed = true;
                }

                if (settled is not null)
                {
                    await RecordSubscriptionLinkAsync(claim, settled, token);
                    return MapSubscription(settled, existing: true);
                }

                if (settleFailed)
                {
                    // The outcome is still unknown; keep the claim so the next attempt recovers
                    // via the deterministic reference instead of creating a second subscription.
                    throw;
                }

                await ReleaseSubscriptionClaimAsync(claim, token);
                throw;
            }
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> GetSubscriptionsForUserAsync(string eshopUserId, CancellationToken cancellationToken)
    {
        return await BoundedAsync<IReadOnlyList<SubscriptionInfo>>(async token =>
        {
            var reference = CustomerReference(eshopUserId);
            var customer = await ReadCustomerOrNullAsync(reference, token);
            if (customer?.Customer.Id is not int customerId)
            {
                // A read never creates a customer: no Maxio customer means no subscriptions.
                return Array.Empty<SubscriptionInfo>();
            }

            await RecordCustomerLinkAsync(eshopUserId, reference, customerId, token);

            IReadOnlyList<SubscriptionResponse> subscriptions;
            try
            {
                subscriptions = await _client.Customers.ListCustomerSubscriptions(
                    new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                    cancellationToken: token);
            }
            catch (ApiException<RawError> ex)
            {
                _logger.LogError(ex, "Maxio rejected listing subscriptions for customer {CustomerId} (HTTP {Status}): {Body}",
                    customerId, (int)ex.StatusCode, SafeBody(ex.Error));
                throw new MaxioBillingException(
                    $"The billing provider rejected listing the account's subscriptions (HTTP {(int)ex.StatusCode}).",
                    (int)ex.StatusCode, ex);
            }
            catch (ResponseDeserializationException ex)
            {
                throw MapDeserialization(ex);
            }
            catch (SdkException ex)
            {
                throw MapTransport(ex);
            }

            return subscriptions
                .Where(response => response.Subscription is not null)
                .Select(response => MapSubscription(response.Subscription!, existing: true))
                .ToList();
        }, cancellationToken);
    }

    // Plans

    private async Task<IReadOnlyList<SubscriptionPlanInfo>> FetchPlansAsync(CancellationToken token)
    {
        var familyId = $"handle:{_options.ProductFamilyHandle}";
        var plans = new List<SubscriptionPlanInfo>();

        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<ProductResponse> products;
            try
            {
                products = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = familyId,
                        Page = page,
                        PerPage = PlanPageSize
                    },
                    cancellationToken: token);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var familyNotFound))
                {
                    _logger.LogError(ex, "Maxio product family not found when listing plans: {Body}", familyNotFound);
                    throw new MaxioBillingException(
                        $"The configured product family was not found at the billing provider. Check the '{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)}' setting.",
                        (int)ex.StatusCode, ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    _logger.LogError(ex, "Maxio rejected listing plans (HTTP {Status}): {Body}", (int)raw.StatusCode, SafeBody(raw));
                    throw new MaxioBillingException(
                        $"The billing provider rejected listing the subscription plans (HTTP {(int)raw.StatusCode}).",
                        (int)raw.StatusCode, ex);
                }

                _logger.LogError(ex, "Maxio listing plans failed with an unrecognised error shape (HTTP {Status}).", (int)ex.StatusCode);
                throw new MaxioBillingException(
                    "The billing provider returned an unrecognised error while listing the subscription plans.",
                    (int)ex.StatusCode, ex);
            }
            catch (ResponseDeserializationException ex)
            {
                throw MapDeserialization(ex);
            }
            catch (SdkException ex)
            {
                throw MapTransport(ex);
            }

            foreach (var response in products)
            {
                var product = response.Product;
                var familyHandle = product.ProductFamily?.Handle;
                if (!string.IsNullOrWhiteSpace(familyHandle) &&
                    !string.Equals(familyHandle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                plans.Add(new SubscriptionPlanInfo
                {
                    MaxioProductId = product.Id ?? 0,
                    Handle = product.Handle ?? string.Empty,
                    Name = product.Name ?? product.Handle ?? string.Empty,
                    Description = product.Description,
                    PriceInCents = product.PriceInCents ?? 0,
                    Interval = product.Interval,
                    IntervalUnit = product.IntervalUnit?.Value,
                    RequiresPaymentProfile = product.RequireCreditCard == true
                });
            }

            if (products.Count < PlanPageSize)
            {
                break;
            }

            if (page == MaxPlanPages)
            {
                _logger.LogWarning("Plan listing stopped at the {MaxPlanPages}-page cap ({Count} plans); a larger catalog would be truncated",
                    MaxPlanPages, plans.Count);
            }
        }

        return plans;
    }

    // Customer ensure (idempotent)

    private async Task<int> EnsureCustomerAsync(string eshopUserId, string email, CancellationToken token)
    {
        var reference = CustomerReference(eshopUserId);

        var existingLink = await _identityDb.MaxioCustomerLinks.FindAsync(new object[] { eshopUserId }, token);
        if (existingLink?.MaxioCustomerId is int recordedId)
        {
            return recordedId;
        }

        var customer = await ReadCustomerOrNullAsync(reference, token);
        if (customer?.Customer.Id is int foundId)
        {
            await RecordCustomerLinkAsync(eshopUserId, reference, foundId, token);
            return foundId;
        }

        // Claim: the primary key refuses a concurrent double-ensure before either caller
        // reaches the provider. A row still present from an earlier attempt whose outcome
        // never settled is reused as the claim instead of re-added.
        var claim = await _identityDb.MaxioCustomerLinks.FindAsync(new object[] { eshopUserId }, token);
        if (claim is null)
        {
            claim = new MaxioCustomerLink
            {
                EShopUserId = eshopUserId,
                MaxioReference = reference,
                CreatedAt = DateTimeOffset.UtcNow
            };
            try
            {
                _identityDb.MaxioCustomerLinks.Add(claim);
                await _identityDb.SaveChangesAsync(token);
            }
            catch (DbUpdateException)
            {
                _identityDb.ChangeTracker.Clear();
                claim = await _identityDb.MaxioCustomerLinks.FindAsync(new object[] { eshopUserId }, token);
            }
            catch (ArgumentException)
            {
                _identityDb.ChangeTracker.Clear();
                claim = await _identityDb.MaxioCustomerLinks.FindAsync(new object[] { eshopUserId }, token);
            }

            if (claim is null)
            {
                return await AwaitConcurrentCustomerAsync(eshopUserId, reference, token);
            }
        }

        try
        {
            var createdId = await CreateCustomerViaProviderAsync(eshopUserId, email, reference, token);
            claim.MaxioCustomerId = createdId;
            await _identityDb.SaveChangesAsync(token);
            return createdId;
        }
        catch (MaxioBillingException ex) when (ex.StatusCode is not null)
        {
            // A refused signup created nothing — unless the provider rejected our deterministic
            // reference as a duplicate, which means the customer already exists (a concurrent
            // ensure won the race). Settle by reference before reporting a failure.
            if (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
            {
                var racedCustomer = await ReadCustomerOrNullAsync(reference, token);
                if (racedCustomer?.Customer.Id is int racedId)
                {
                    await RecordCustomerLinkAsync(eshopUserId, reference, racedId, token);
                    _logger.LogInformation(
                        "Maxio customer {CustomerId} already held reference {Reference}; returning it (concurrent ensure)",
                        racedId, reference);
                    return racedId;
                }
            }

            await ReleaseCustomerClaimAsync(claim, token);
            throw;
        }
        catch (MaxioBillingException ex) when (ex.StatusCode is null)
        {
            CustomerResponse? settled = null;
            var settleFailed = false;
            try
            {
                settled = await SettleCustomerByReferenceAsync(reference, token);
            }
            catch (MaxioBillingException)
            {
                settleFailed = true;
            }

            if (settled?.Customer.Id is int settledId)
            {
                claim.MaxioCustomerId = settledId;
                await _identityDb.SaveChangesAsync(token);
                return settledId;
            }

            if (settleFailed)
            {
                // The outcome is still unknown; keep the claim so the next attempt recovers
                // via the deterministic reference instead of creating a second customer.
                throw;
            }

            await ReleaseCustomerClaimAsync(claim, token);
            throw;
        }
    }

    private async Task<int> CreateCustomerViaProviderAsync(string eshopUserId, string email, string reference, CancellationToken token)
    {
        var (firstName, lastName) = DeriveCustomerNames(email);

        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = email,
                            Reference = reference
                        }
                    }
                },
                cancellationToken: token);

            if (response.Customer.Id is not int createdId)
            {
                throw new MaxioBillingException("The billing provider returned a customer without an id.", (int)HttpStatusCode.BadGateway);
            }

            _logger.LogInformation("Ensured Maxio customer {CustomerId} (reference {Reference}) for user {UserId}",
                createdId, reference, eshopUserId);
            return createdId;
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                var detail = body.Errors is null ? string.Empty : DescribeErrors(body.Errors);
                _logger.LogError(ex, "Maxio rejected customer creation (HTTP {Status}): {Detail}", (int)ex.StatusCode, detail);
                throw new MaxioBillingException(
                    $"The billing provider rejected creating the billing customer.{(detail.Length > 0 ? " " + detail : string.Empty)}",
                    (int)ex.StatusCode, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError(ex, "Maxio customer creation failed (HTTP {Status}): {Body}", (int)raw.StatusCode, SafeBody(raw));
                throw new MaxioBillingException(
                    $"The billing provider rejected creating the billing customer (HTTP {(int)raw.StatusCode}).",
                    (int)raw.StatusCode, ex);
            }

            _logger.LogError(ex, "Maxio customer creation failed with an unrecognised error shape (HTTP {Status}).", (int)ex.StatusCode);
            throw new MaxioBillingException(
                "The billing provider rejected creating the billing customer.",
                (int)ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw MapDeserialization(ex);
        }
        catch (SdkException ex)
        {
            throw MapTransport(ex);
        }
    }

    private async Task<SubscriptionResponse> CreateSubscriptionViaProviderAsync(string planHandle, int customerId, string reference, CancellationToken token)
    {
        // The seeded plans require no payment method, so signup must not attempt an
        // automatic card charge. Remittance collection (Relationship Invoicing) is tried
        // first; a site on the legacy Statements architecture refuses it with a 422 and
        // invoice collection is tried once. A refused signup creates nothing at the
        // provider, so the deterministic reference stays unused.
        var attempts = new[] { CollectionMethod.Remittance, CollectionMethod.Invoice };
        MaxioBillingException? lastError = null;

        foreach (var collectionMethod in attempts)
        {
            try
            {
                return await CreateSubscriptionWithCollectionMethodAsync(planHandle, customerId, reference, collectionMethod, token);
            }
            catch (MaxioBillingException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity &&
                                                   collectionMethod != attempts[^1])
            {
                lastError = ex;
                _logger.LogWarning("Maxio refused signup with {Attempted} collection (HTTP 422); retrying with {Fallback} collection",
                    collectionMethod.Value, attempts[^1].Value);
            }
        }

        throw lastError!;
    }

    private async Task<SubscriptionResponse> CreateSubscriptionWithCollectionMethodAsync(
        string planHandle, int customerId, string reference, CollectionMethod collectionMethod, CancellationToken token)
    {
        try
        {
            return await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerId = customerId,
                            Reference = reference,
                            PaymentCollectionMethod = collectionMethod
                        }
                    }
                },
                cancellationToken: token);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                var detail = body.Errors is null ? string.Empty : string.Join("; ", body.Errors);
                _logger.LogError(ex, "Maxio rejected subscription creation (HTTP {Status}): {Detail}", (int)ex.StatusCode, detail);
                throw new MaxioBillingException(
                    $"The billing provider rejected subscribing to plan '{planHandle}'.{(detail.Length > 0 ? " " + detail : string.Empty)}",
                    (int)ex.StatusCode, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError(ex, "Maxio subscription creation failed (HTTP {Status}): {Body}", (int)raw.StatusCode, SafeBody(raw));
                throw new MaxioBillingException(
                    $"The billing provider rejected subscribing to plan '{planHandle}' (HTTP {(int)raw.StatusCode}).",
                    (int)raw.StatusCode, ex);
            }

            _logger.LogError(ex, "Maxio subscription creation failed with an unrecognised error shape (HTTP {Status}).", (int)ex.StatusCode);
            throw new MaxioBillingException(
                $"The billing provider rejected subscribing to plan '{planHandle}'.",
                (int)ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw MapDeserialization(ex);
        }
        catch (SdkException ex)
        {
            throw MapTransport(ex);
        }
    }

    private async Task<int> AwaitConcurrentCustomerAsync(string eshopUserId, string reference, CancellationToken token)
    {
        for (var attempt = 0; attempt < ConcurrentWaitAttempts; attempt++)
        {
            var link = await _identityDb.MaxioCustomerLinks.FindAsync(new object[] { eshopUserId }, token);
            if (link?.MaxioCustomerId is int recordedId)
            {
                return recordedId;
            }

            var customer = await ReadCustomerOrNullAsync(reference, token);
            if (customer?.Customer.Id is int foundId)
            {
                await RecordCustomerLinkAsync(eshopUserId, reference, foundId, token);
                return foundId;
            }

            await Task.Delay(ReconcileDelay, token);
        }

        throw new MaxioBillingException(
            "Another request is currently ensuring the billing customer for this account; try again.",
            (int)HttpStatusCode.Conflict);
    }

    // Reads

    private async Task<CustomerResponse?> ReadCustomerOrNullAsync(string reference, CancellationToken token)
    {
        try
        {
            return await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: token);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (ApiException<RawError> ex)
        {
            _logger.LogError(ex, "Maxio rejected the customer lookup for reference {Reference} (HTTP {Status}): {Body}",
                reference, (int)ex.StatusCode, SafeBody(ex.Error));
            throw new MaxioBillingException(
                $"The billing provider rejected looking up the billing customer (HTTP {(int)ex.StatusCode}).",
                (int)ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw MapDeserialization(ex);
        }
        catch (SdkException ex)
        {
            throw MapTransport(ex);
        }
    }

    private async Task<SubscriptionResponse?> FindSubscriptionOrNullAsync(string reference, CancellationToken token)
    {
        try
        {
            return await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: token);
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            // 404 (no content) means no subscription carries this reference — a miss, not a failure.
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError(ex, "Maxio rejected the subscription lookup for reference {Reference} (HTTP {Status}): {Body}",
                    reference, (int)raw.StatusCode, SafeBody(raw));
                throw new MaxioBillingException(
                    $"The billing provider rejected looking up the subscription (HTTP {(int)raw.StatusCode}).",
                    (int)raw.StatusCode, ex);
            }

            _logger.LogError(ex, "Maxio subscription lookup failed with an unrecognised error shape (HTTP {Status}).", (int)ex.StatusCode);
            throw new MaxioBillingException(
                "The billing provider returned an unrecognised error while looking up the subscription.",
                (int)ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw MapDeserialization(ex);
        }
        catch (SdkException ex)
        {
            throw MapTransport(ex);
        }
    }

    private async Task<SubscriptionResponse?> ReadSubscriptionOrNullAsync(int subscriptionId, CancellationToken token)
    {
        try
        {
            return await _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId },
                cancellationToken: token);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (ApiException<RawError> ex)
        {
            _logger.LogError(ex, "Maxio rejected reading subscription {SubscriptionId} (HTTP {Status}): {Body}",
                subscriptionId, (int)ex.StatusCode, SafeBody(ex.Error));
            throw new MaxioBillingException(
                $"The billing provider rejected reading the subscription (HTTP {(int)ex.StatusCode}).",
                (int)ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw MapDeserialization(ex);
        }
        catch (SdkException ex)
        {
            throw MapTransport(ex);
        }
    }

    // Ambiguous-outcome settlement

    private async Task<CustomerResponse?> SettleCustomerByReferenceAsync(string reference, CancellationToken token)
    {
        for (var attempt = 0; attempt < ReconcileAttempts; attempt++)
        {
            var customer = await ReadCustomerOrNullAsync(reference, token);
            if (customer is not null)
            {
                return customer;
            }

            await Task.Delay(ReconcileDelay, token);
        }

        return null;
    }

    private async Task<Subscription?> SettleSubscriptionByReferenceAsync(string reference, CancellationToken token)
    {
        for (var attempt = 0; attempt < ReconcileAttempts; attempt++)
        {
            var found = await FindSubscriptionOrNullAsync(reference, token);
            if (found?.Subscription is not null)
            {
                return found.Subscription;
            }

            await Task.Delay(ReconcileDelay, token);
        }

        return null;
    }

    private async Task<SubscriptionInfo> AwaitConcurrentSubscriptionAsync(
        string eshopUserId, string planHandle, string subscriptionReference, CancellationToken token)
    {
        for (var attempt = 0; attempt < ConcurrentWaitAttempts; attempt++)
        {
            var link = await FindSubscriptionLinkAsync(eshopUserId, planHandle, token);
            if (link?.MaxioSubscriptionId is int recordedId)
            {
                var recorded = await ReadSubscriptionOrNullAsync(recordedId, token);
                if (recorded?.Subscription is not null)
                {
                    return MapSubscription(recorded.Subscription, existing: true);
                }
            }

            var found = await FindSubscriptionOrNullAsync(subscriptionReference, token);
            if (found?.Subscription is not null)
            {
                if (link is not null)
                {
                    await RecordSubscriptionLinkAsync(link, found.Subscription, token);
                }
                return MapSubscription(found.Subscription, existing: true);
            }

            await Task.Delay(ReconcileDelay, token);
        }

        throw new MaxioBillingException(
            "Another request is currently subscribing this account to this plan; try again.",
            (int)HttpStatusCode.Conflict);
    }

    // Claim-store helpers

    private async Task<MaxioSubscriptionLink?> FindSubscriptionLinkAsync(string eshopUserId, string planHandle, CancellationToken token)
    {
        return await _identityDb.MaxioSubscriptionLinks.FindAsync(
            new object[] { eshopUserId, planHandle }, token);
    }

    private async Task RecordCustomerLinkAsync(string eshopUserId, string reference, int maxioCustomerId, CancellationToken token)
    {
        var link = await _identityDb.MaxioCustomerLinks.FindAsync(new object[] { eshopUserId }, token);
        if (link is null)
        {
            link = new MaxioCustomerLink
            {
                EShopUserId = eshopUserId,
                MaxioReference = reference,
                MaxioCustomerId = maxioCustomerId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _identityDb.MaxioCustomerLinks.Add(link);
        }
        else
        {
            link.MaxioCustomerId = maxioCustomerId;
        }

        try
        {
            await _identityDb.SaveChangesAsync(token);
        }
        catch (DbUpdateException)
        {
            // A concurrent request recorded the link first; the row in the store is the same fact.
            _identityDb.ChangeTracker.Clear();
        }
        catch (ArgumentException)
        {
            _identityDb.ChangeTracker.Clear();
        }
    }

    private async Task RecordSubscriptionLinkAsync(MaxioSubscriptionLink link, Subscription subscription, CancellationToken token)
    {
        link.MaxioSubscriptionId = subscription.Id;
        link.State = subscription.State?.Value;

        try
        {
            await _identityDb.SaveChangesAsync(token);
        }
        catch (DbUpdateException)
        {
            _identityDb.ChangeTracker.Clear();
        }
        catch (ArgumentException)
        {
            _identityDb.ChangeTracker.Clear();
        }
    }

    private async Task ReleaseCustomerClaimAsync(MaxioCustomerLink claim, CancellationToken token)
    {
        _identityDb.MaxioCustomerLinks.Remove(claim);
        try
        {
            await _identityDb.SaveChangesAsync(token);
        }
        catch (DbUpdateException)
        {
            _identityDb.ChangeTracker.Clear();
        }
        catch (ArgumentException)
        {
            _identityDb.ChangeTracker.Clear();
        }
    }

    private async Task ReleaseSubscriptionClaimAsync(MaxioSubscriptionLink claim, CancellationToken token)
    {
        _identityDb.MaxioSubscriptionLinks.Remove(claim);
        try
        {
            await _identityDb.SaveChangesAsync(token);
        }
        catch (DbUpdateException)
        {
            _identityDb.ChangeTracker.Clear();
        }
        catch (ArgumentException)
        {
            _identityDb.ChangeTracker.Clear();
        }
    }

    // Mapping and translation

    private static SubscriptionInfo MapSubscription(Subscription subscription, bool existing) =>
        new()
        {
            MaxioSubscriptionId = subscription.Id ?? 0,
            PlanHandle = subscription.Product?.Handle ?? subscription.Reference ?? string.Empty,
            PlanName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents ?? 0,
            State = subscription.State?.Value ?? "unknown",
            NextBillingAt = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt,
            Existing = existing
        };

    private static string DescribeErrors(Errors1 errors) =>
        errors.TryGetListOfString(out var list) ? string.Join("; ", list) : "validation error";

    private static string SafeBody(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return string.IsNullOrEmpty(body) ? string.Empty : body.Length > 500 ? body[..500] : body;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static (string FirstName, string LastName) DeriveCustomerNames(string email)
    {
        var local = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var parts = local.Split('.', '_', '-', '+');
        var firstName = Capitalize(parts.FirstOrDefault());
        var lastName = parts.Length > 1
            ? Capitalize(string.Join(" ", parts.Skip(1).Where(part => part.Length > 0)))
            : "Shopper";
        return (string.IsNullOrWhiteSpace(firstName) ? "eShop" : firstName,
                string.IsNullOrWhiteSpace(lastName) ? "Shopper" : lastName);
    }

    private static string Capitalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private MaxioBillingException MapDeserialization(ResponseDeserializationException ex)
    {
        // 2xx drift and an unreadable error body are different facts; both keep the provider status.
        _logger.LogError(ex, "Maxio returned a response that could not be processed (HTTP {Status}, target {TargetType}).",
            (int)ex.StatusCode, ex.TargetType);
        return new MaxioBillingException(
            "The billing provider returned a response that could not be processed.",
            (int)ex.StatusCode, ex);
    }

    private MaxioBillingException MapTransport(SdkException ex)
    {
        _logger.LogError(ex, "The Maxio call {Method} {Uri} failed in transport: {Message}", ex.Method, ex.RequestUri, ex.Message);
        return new MaxioBillingException(
            "The billing provider is unreachable or did not answer in time.", null, ex);
    }

    private static MaxioBillingException MapAuth(AuthSchemeException ex) =>
        new("The billing provider credentials could not be applied.", null, ex);

    // Whole-call budget: every public method bounds its total duration at one place.
    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RequestBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (AuthSchemeException ex)
        {
            throw MapAuth(ex);
        }
    }

    // Deterministic, app-owned references

    private static string CustomerReference(string eshopUserId) => $"eshop-user-{eshopUserId}";

    private static string SubscriptionReference(string eshopUserId, string planHandle) =>
        $"eshop-sub-{eshopUserId}:{planHandle}";
}