using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Microsoft.eShopWeb.ApplicationCore.Models.MaxioBilling;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.MaxioBilling;

/// <summary>
/// Orchestrates subscriptions for eShopOnWeb users with Maxio Advanced Billing
/// as the system of record.
///
/// Idempotency design:
///   - The Maxio customer reference is the eShopOnWeb user id, and the Maxio
///     subscription reference is "{userId}:{planHandle}" — both are unique keys
///     on Maxio's side, so a double-click can never create duplicates even
///     across app restarts.
///   - The (userId, planHandle) pair is also persisted locally (unique index)
///     for fast lookups and a second layer of duplicate protection.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const string PlansCacheKeyPrefix = "maxio-plans:";

    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IMaxioBillingClient _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IRepository<Subscription> subscriptionRepository,
        IMaxioBillingClient maxioClient, MaxioSettings settings, IMemoryCache cache,
        ILogger<MaxioSubscriptionService> logger)
    {
        _subscriptionRepository = subscriptionRepository;
        _maxioClient = maxioClient;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = PlansCacheKeyPrefix + _settings.ProductFamilyHandle.ToLowerInvariant();

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlanInfo>? cached) && cached is not null)
        {
            return cached;
        }

        var products = await _maxioClient.ListProductsForFamilyAsync(cancellationToken);

        var plans = products
            .OrderBy(p => p.PriceInCents)
            .Select(p => new SubscriptionPlanInfo(p.Handle, p.Name, p.PriceInCents, p.Interval, p.IntervalUnit, p.ProductFamilyHandle))
            .ToList()
            .AsReadOnly();

        _cache.Set(cacheKey, plans, TimeSpan.FromSeconds(60));
        return plans;
    }

    public async Task<Result<Subscription>> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Result<Subscription>.Error("A user id is required.");
        if (string.IsNullOrWhiteSpace(email)) return Result<Subscription>.Error("A user email is required.");
        if (string.IsNullOrWhiteSpace(planHandle)) return Result<Subscription>.Error("A plan handle is required.");

        try
        {
            var plan = (await GetPlansAsync(cancellationToken))
                .FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

            if (plan is null)
            {
                return Result<Subscription>.NotFound($"No subscription plan with handle '{planHandle}' exists.");
            }

            // 1. Local fast path: already subscribed per the persisted mapping.
            var existing = (await _subscriptionRepository.ListAsync(
                    new SubscriptionForUserAndPlanSpecification(userId, planHandle), cancellationToken))
                .FirstOrDefault();

            if (existing is not null)
            {
                _logger.LogInformation("User {UserId} is already subscribed to plan {PlanHandle} (Maxio subscription {MaxioSubscriptionId}).",
                    userId, planHandle, existing.MaxioSubscriptionId);
                return Result<Subscription>.Success(existing);
            }

            // 2. Resolve (or create, idempotently) the Maxio customer for this user.
            //    The customer reference is the eShopOnWeb user id — unique on Maxio's side.
            var customer = await _maxioClient.FindCustomerByReferenceAsync(userId, cancellationToken)
                           ?? await _maxioClient.CreateCustomerAsync(BuildCustomerCreate(userId, email), cancellationToken);

            // 3. Look for the user's existing Maxio subscription for this plan.
            //    (Customer-scoped listing with exact reference matching — verified reliable.)
            var subscriptionReference = BuildSubscriptionReference(userId, planHandle);
            var customerSubscriptions = await _maxioClient.ListSubscriptionsByCustomerAsync(customer.Id, cancellationToken);
            var maxioSubscription = customerSubscriptions.FirstOrDefault(s => string.Equals(s.Reference, subscriptionReference, StringComparison.Ordinal));

            if (maxioSubscription is not null && IsActive(maxioSubscription.State))
            {
                var healed = await UpsertLocalAsync(userId, maxioSubscription, cancellationToken);
                return Result<Subscription>.Success(healed);
            }

            // 4. If a prior subscription with the same reference was canceled/expired,
            //    enroll under a fresh reference so the user can subscribe again.
            if (maxioSubscription is not null && !IsActive(maxioSubscription.State))
            {
                subscriptionReference = $"{subscriptionReference}:{DateTime.UtcNow:yyyyMMddHHmmss}";
            }

            // 5. Enroll in Maxio. A concurrent double-click loses the race on Maxio's
            //    unique reference; the existing subscription on this customer is the
            //    correct answer.
            try
            {
                maxioSubscription = await _maxioClient.CreateSubscriptionAsync(
                    new MaxioSubscriptionCreate(plan.Handle, customer.Id, subscriptionReference), cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity &&
                                               ex.Errors.Any(e => e.Contains("Reference", StringComparison.OrdinalIgnoreCase) &&
                                                                  e.Contains("unique", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("Subscription reference {Reference} already taken at Maxio; fetching the existing subscription from the customer's records.", subscriptionReference);
                maxioSubscription = (await _maxioClient.ListSubscriptionsByCustomerAsync(customer.Id, cancellationToken))
                    .FirstOrDefault(s => string.Equals(s.Reference, subscriptionReference, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException(
                        $"Maxio reports subscription reference '{subscriptionReference}' as taken, but it cannot be found on customer {customer.Id}.");
            }

            var subscription = await UpsertLocalAsync(userId, maxioSubscription, cancellationToken);

            _logger.LogInformation("User {UserId} subscribed to plan {PlanHandle}: Maxio subscription {MaxioSubscriptionId} ({State}), next billing {NextBilling}.",
                userId, planHandle, maxioSubscription.Id, maxioSubscription.State, maxioSubscription.NextBillingDateUtc);

            return Result<Subscription>.Success(subscription);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Maxio API error while subscribing user {UserId} to plan {PlanHandle}.", userId, planHandle);
            return Result<Subscription>.Error(
                $"The billing system (Maxio) rejected the request: {string.Join("; ", ex.Errors)}");
        }
    }

    public async Task<IReadOnlyList<Subscription>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _subscriptionRepository.ListAsync(new SubscriptionsForUserSpecification(userId), cancellationToken);
    }

    private async Task<Subscription> UpsertLocalAsync(string userId, MaxioSubscription maxioSubscription, CancellationToken cancellationToken)
    {
        var existing = (await _subscriptionRepository.ListAsync(
                new SubscriptionForUserAndPlanSpecification(userId, maxioSubscription.ProductHandle), cancellationToken))
            .FirstOrDefault();

        if (existing is not null)
        {
            existing.SyncFromMaxio(maxioSubscription.ProductName, maxioSubscription.State,
                maxioSubscription.ProductPriceInCents, maxioSubscription.Currency,
                maxioSubscription.Interval, maxioSubscription.IntervalUnit,
                maxioSubscription.NextBillingDateUtc, maxioSubscription.CustomerId, maxioSubscription.Id);
            await _subscriptionRepository.UpdateAsync(existing, cancellationToken);
            return existing;
        }

        var subscription = new Subscription(
            userId: userId,
            planHandle: maxioSubscription.ProductHandle,
            maxioCustomerReference: userId,
            maxioCustomerId: maxioSubscription.CustomerId,
            maxioSubscriptionReference: maxioSubscription.Reference ?? BuildSubscriptionReference(userId, maxioSubscription.ProductHandle),
            maxioSubscriptionId: maxioSubscription.Id,
            planName: maxioSubscription.ProductName,
            state: maxioSubscription.State,
            priceInCents: maxioSubscription.ProductPriceInCents,
            currency: maxioSubscription.Currency,
            billingInterval: maxioSubscription.Interval,
            billingIntervalUnit: maxioSubscription.IntervalUnit,
            nextBillingDateUtc: maxioSubscription.NextBillingDateUtc);

        try
        {
            await _subscriptionRepository.AddAsync(subscription, cancellationToken);
            return subscription;
        }
        catch (Exception ex)
        {
            // A concurrent double-click may have raced us to the unique
            // (userId, planHandle) index; the winner's row is authoritative.
            _logger.LogWarning(ex, "Concurrent insert of subscription for user {UserId} plan {PlanHandle}; re-reading the persisted row.", userId, maxioSubscription.ProductHandle);
            var raced = (await _subscriptionRepository.ListAsync(
                    new SubscriptionForUserAndPlanSpecification(userId, maxioSubscription.ProductHandle), cancellationToken))
                .FirstOrDefault();

            if (raced is null)
            {
                throw;
            }

            return raced;
        }
    }

    private static MaxioCustomerCreate BuildCustomerCreate(string userId, string email)
    {
        var localPart = email.Split('@', 2)[0];
        if (string.IsNullOrWhiteSpace(localPart))
        {
            localPart = "Customer";
        }

        return new MaxioCustomerCreate(
            Reference: userId,
            Email: email,
            FirstName: char.ToUpperInvariant(localPart[0]) + localPart[1..],
            LastName: "Subscriber",
            Organization: "eShopOnWeb");
    }

    private static string BuildSubscriptionReference(string userId, string planHandle) => $"{userId}:{planHandle}";

    private static bool IsActive(string state) =>
        state is "active" or "trialing" or "pending";
}