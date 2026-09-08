using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates subscriptions against Maxio Advanced Billing (the billing system of record).
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    // Reference prefix identifying customers created by this app inside the Maxio site.
    private const string CUSTOMER_REFERENCE_PREFIX = "eshoponweb:";

    // Serialize subscribe calls per user so a double-click cannot slip two create calls through.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserGates = new();

    private readonly IMaxioAdvancedBillingClient _maxioClient;
    private readonly IRepository<MaxioSubscription> _subscriptionRepository;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly MaxioSettings _settings;

    public SubscriptionService(IMaxioAdvancedBillingClient maxioClient,
        IRepository<MaxioSubscription> subscriptionRepository,
        IAppLogger<SubscriptionService> logger,
        MaxioSettings settings)
    {
        _maxioClient = maxioClient;
        _subscriptionRepository = subscriptionRepository;
        _logger = logger;
        _settings = settings;
    }

    public async Task<IReadOnlyList<MaxioPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _maxioClient.GetPlansForProductFamilyAsync(ResolveProductFamilyHandle(), cancellationToken);
        return plans.Where(p => !p.Archived).ToList();
    }

    public async Task<SubscriptionResult> SubscribeAsync(string userId, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        var gate = UserGates.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var plan = (await GetAvailablePlansAsync(cancellationToken))
                .SingleOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
                ?? throw new SubscriptionPlanNotFoundException(planHandle);

            var customer = await EnsureCustomerAsync(userId, cancellationToken);

            var existingSubscriptions = await _maxioClient.GetSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
            var liveSubscription = existingSubscriptions.FirstOrDefault(s => s.ProductId == plan.ProductId && s.IsLive);

            MaxioSubscriptionInfo subscription;
            bool newlyCreated;
            if (liveSubscription != null)
            {
                _logger.LogInformation("User {UserId} already holds live Maxio subscription {SubscriptionId} for plan {PlanHandle}; not enrolling again.",
                    userId, liveSubscription.Id, plan.Handle);
                subscription = liveSubscription;
                newlyCreated = false;
            }
            else
            {
                subscription = await _maxioClient.CreateSubscriptionAsync(customer.Id, plan, cancellationToken);
                newlyCreated = true;
                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}.",
                    subscription.Id, userId, plan.Handle);
            }

            await RecordLocallyAsync(userId, customer, subscription, plan, cancellationToken);

            return ToResult(subscription, plan.Name, plan.IntervalUnit, newlyCreated);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionResult>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));

        var customer = await _maxioClient.FindCustomerByReferenceAsync(BuildReference(userId), cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionResult>();
        }

        var plans = await GetAvailablePlansAsync(cancellationToken);
        var subscriptions = await _maxioClient.GetSubscriptionsForCustomerAsync(customer.Id, cancellationToken);

        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? s.NextBillingAt)
            .Select(s =>
            {
                var plan = plans.FirstOrDefault(p => p.ProductId == s.ProductId);
                return ToResult(s, plan?.Name ?? s.ProductName ?? s.ProductHandle ?? "Unknown plan", plan?.IntervalUnit ?? string.Empty, newlyCreated: false);
            })
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userId, CancellationToken cancellationToken)
    {
        var reference = BuildReference(userId);

        var existing = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        // Maxio enforces uniqueness of the customer reference, so the create itself is the
        // idempotency guarantee; on the (rare) race where another request won, re-read instead.
        var firstName = userId.Contains('@') ? userId[..userId.IndexOf('@')] : userId;
        try
        {
            return await _maxioClient.CreateCustomerAsync(reference, firstName, "eShopOnWeb Subscriber", email: userId, cancellationToken);
        }
        catch (MaxioBillingProviderException ex) when (ex.DuplicateCustomerReference)
        {
            var winner = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken)
                ?? throw new MaxioBillingProviderException($"Maxio reported a duplicate customer reference '{reference}' but the customer could not be read back.", ex.StatusCode);
            return winner;
        }
    }

    private async Task RecordLocallyAsync(string userId, MaxioCustomer customer, MaxioSubscriptionInfo subscription, MaxioPlan plan, CancellationToken cancellationToken)
    {
        var localSubscriptions = await _subscriptionRepository.ListAsync(new MaxioSubscriptionsByUserIdSpecification(userId), cancellationToken);

        var tracked = localSubscriptions.FirstOrDefault(s => s.MaxioSubscriptionId == subscription.Id);
        if (tracked != null)
        {
            tracked.SyncFromBillingProvider(subscription.State, subscription.NextBillingAt);
            await _subscriptionRepository.UpdateAsync(tracked, cancellationToken);
            return;
        }

        var newTracking = new MaxioSubscription(
            userId,
            customer.Id,
            subscription.Id,
            plan.Handle,
            plan.Name,
            subscription.Price,
            plan.IntervalUnit,
            subscription.State,
            subscription.NextBillingAt,
            subscription.CreatedAt ?? DateTimeOffset.UtcNow);

        await _subscriptionRepository.AddAsync(newTracking, cancellationToken);
    }

    private static SubscriptionResult ToResult(MaxioSubscriptionInfo subscription, string planName, string intervalUnit, bool newlyCreated)
    {
        return new SubscriptionResult
        {
            MaxioSubscriptionId = subscription.Id,
            MaxioCustomerId = subscription.CustomerId,
            PlanHandle = subscription.ProductHandle ?? string.Empty,
            PlanName = planName,
            Price = subscription.Price,
            IntervalUnit = intervalUnit,
            State = subscription.State,
            NextBillingAt = subscription.NextBillingAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt,
            NewlyCreated = newlyCreated
        };
    }

    private string ResolveProductFamilyHandle()
    {
        Guard.Against.NullOrWhiteSpace(_settings.ProductFamilyHandle, nameof(MaxioSettings.ProductFamilyHandle),
            "No Maxio product family is configured. Set 'Maxio:ProductFamilyHandle'.");
        return _settings.ProductFamilyHandle;
    }

    private static string BuildReference(string userId) => CUSTOMER_REFERENCE_PREFIX + userId;
}
