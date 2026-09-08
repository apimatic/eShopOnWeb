using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates subscription flows against Maxio Advanced Billing.
/// Maxio is the billing system of record; the eShopOnWeb user id is used as the
/// Maxio customer "reference" so the mapping survives restarts without local storage.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Subscription states that mean the customer already holds this plan,
    // so a repeated subscribe must not create a second Maxio subscription.
    // Only terminal states (canceled/expired) allow re-subscribing.
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "pending", "on_hold", "unpaid", "suspended"
    };

    // Serializes subscribe calls per user so a double-click can never create
    // two Maxio customers or subscriptions even under concurrent requests.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);

    private const string FamilyIdCacheKey = "maxio-product-family-id";
    private static readonly TimeSpan FamilyCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PlansCacheDuration = TimeSpan.FromMinutes(1);

    private readonly IMaxioClient _maxioClient;
    private readonly IMemoryCache _cache;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioClient maxioClient,
        IMemoryCache cache,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<SubscriptionPlanInfo>>> GetPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            var plans = await ListPlansCoreAsync(cancellationToken);
            return Result<IReadOnlyList<SubscriptionPlanInfo>>.Success(
                plans.Select(MapPlan).ToList());
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Failed to list subscription plans from Maxio");
            return Result<IReadOnlyList<SubscriptionPlanInfo>>.Error($"Unable to load subscription plans: {ex.Message}");
        }
    }

    public async Task<Result<SubscriptionSummary>> SubscribeAsync(
        string userId,
        string username,
        string email,
        string planHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result<SubscriptionSummary>.Error("User id is required.");
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return Result<SubscriptionSummary>.Invalid(new List<ValidationError>
            {
                new() { Identifier = nameof(planHandle), ErrorMessage = "Plan handle is required." }
            });
        }

        var userLock = UserLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            // 1. Validate the requested plan against the configured product family.
            var planResult = await FindPlanAsync(planHandle.Trim(), cancellationToken);
            if (!planResult.IsSuccess)
            {
                return Result<SubscriptionSummary>.NotFound($"No subscription plan with handle '{planHandle}' exists.");
            }
            var plan = planResult.Value;

            // 2. Ensure the Maxio customer exists (idempotent by reference = user id).
            var customer = await EnsureCustomerAsync(userId, username, email, cancellationToken);

            // 3. Do not create a second subscription for a plan the user already holds.
            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing != null)
            {
                _logger.LogInformation("User {UserId} already holds subscription {SubscriptionId} for plan {PlanHandle}",
                    userId, existing.Id, plan.Handle);
                return Result<SubscriptionSummary>.Success(MapSubscription(existing, alreadySubscribed: true));
            }

            // 4. Enroll. "remittance" collection allows subscribing without card capture.
            var created = await _maxioClient.CreateSubscriptionAsync(
                new MaxioCreateSubscriptionRequest
                {
                    CustomerId = customer.Id,
                    ProductHandle = plan.Handle,
                    PaymentCollectionMethod = "remittance"
                },
                cancellationToken);

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} (plan {PlanHandle}) for user {UserId}",
                created.Id, plan.Handle, userId);

            return Result<SubscriptionSummary>.Success(MapSubscription(created, alreadySubscribed: false));
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Maxio API failure while subscribing user {UserId} to plan {PlanHandle}", userId, planHandle);
            return Result<SubscriptionSummary>.Error($"The billing system rejected the subscription: {ex.Message}");
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<Result<IReadOnlyList<SubscriptionSummary>>> GetSubscriptionsForUserAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result<IReadOnlyList<SubscriptionSummary>>.Error("User id is required.");
        }

        try
        {
            var customer = await _maxioClient.FindCustomerByReferenceAsync(userId, cancellationToken);
            if (customer == null)
            {
                return Result<IReadOnlyList<SubscriptionSummary>>.Success(new List<SubscriptionSummary>());
            }

            var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            return Result<IReadOnlyList<SubscriptionSummary>>.Success(
                subscriptions.Select(s => MapSubscription(s, alreadySubscribed: false)).ToList());
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Failed to read Maxio subscriptions for user {UserId}", userId);
            return Result<IReadOnlyList<SubscriptionSummary>>.Error($"Unable to load subscriptions: {ex.Message}");
        }
    }

    private async Task<Result<MaxioProduct>> FindPlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var products = await ListPlansCoreAsync(cancellationToken);
        var plan = products.FirstOrDefault(p => p.Handle == planHandle);
        return plan == null
            ? Result<MaxioProduct>.NotFound()
            : Result<MaxioProduct>.Success(plan);
    }

    private async Task<IReadOnlyList<MaxioProduct>> ListPlansCoreAsync(CancellationToken cancellationToken)
    {
        return (await _cache.GetOrCreateAsync(CacheKeys.Plans, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PlansCacheDuration;

            var familyId = await _cache.GetOrCreateAsync(FamilyIdCacheKey, async familyEntry =>
            {
                familyEntry.AbsoluteExpirationRelativeToNow = FamilyCacheDuration;

                var family = await _maxioClient.FindProductFamilyByHandleAsync(_options.ProductFamilyHandle, cancellationToken)
                    ?? throw new MaxioApiException(404,
                        new[] { $"Product family with handle '{_options.ProductFamilyHandle}' was not found." },
                        "resolving the configured product family");

                return family.Id;
            });

            var products = await _maxioClient.ListProductsInFamilyAsync(familyId, cancellationToken);
            // Archived products are not subscribable plans.
            return products.Where(p => p.ArchivedAt == null).ToList();
        }))!;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        string userId,
        string username,
        string email,
        CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var localName = username;
        var atIndex = username.IndexOf('@');
        if (atIndex > 0)
        {
            localName = username[..atIndex];
        }

        _logger.LogInformation("Creating Maxio customer for user {UserId}", userId);
        return await _maxioClient.CreateCustomerAsync(
            new MaxioCreateCustomerRequest
            {
                Reference = userId,
                FirstName = string.IsNullOrWhiteSpace(localName) ? username : localName,
                LastName = "eShopOnWeb Customer",
                Email = email
            },
            cancellationToken);
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            LiveSubscriptionStates.Contains(s.State));
    }

    private static SubscriptionPlanInfo MapPlan(MaxioProduct product) => new(
        product.Id,
        product.Handle,
        product.Name,
        product.Description,
        product.PriceInCents / 100m,
        product.Interval,
        product.IntervalUnit);

    private static SubscriptionSummary MapSubscription(MaxioSubscription subscription, bool alreadySubscribed) => new(
        subscription.Id,
        subscription.Product?.Handle ?? string.Empty,
        subscription.Product?.Name ?? string.Empty,
        subscription.ProductPriceInCents / 100m,
        subscription.Currency,
        subscription.State,
        subscription.State.Equals("canceled", StringComparison.OrdinalIgnoreCase) ? null : subscription.CurrentPeriodEndsAt,
        subscription.CurrentPeriodEndsAt,
        subscription.CreatedAt)
    {
        AlreadySubscribed = alreadySubscribed
    };

    private static class CacheKeys
    {
        public const string Plans = "maxio-plans";
    }
}
