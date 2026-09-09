using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates subscription enrollment. Maxio Advanced Billing is the system of
/// record; a local userId-to-subscription mapping is persisted for lookup.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    // Serializes subscribe attempts per user so a double-click cannot create
    // two customers/subscriptions.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();

    private readonly IMaxioBillingClient _maxio;
    private readonly IRepository<UserSubscription> _subscriptionRepository;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly string _productFamilyHandle;

    public SubscriptionService(IMaxioBillingClient maxio,
        IRepository<UserSubscription> subscriptionRepository,
        IAppLogger<SubscriptionService> logger,
        string productFamilyHandle)
    {
        _maxio = maxio;
        _subscriptionRepository = subscriptionRepository;
        _logger = logger;
        _productFamilyHandle = productFamilyHandle;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxio.ListProductsForFamilyAsync(_productFamilyHandle, cancellationToken);

        return products
            .OrderBy(p => p.PriceInCents)
            .Select(p => new SubscriptionPlan(p.Handle, p.Name, p.Description, p.PriceInCents, "USD", p.IntervalUnit, p.Interval))
            .ToList();
    }

    public async Task<SubscriptionStatus> SubscribeAsync(string userId, string userName, string email, string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
        if (string.IsNullOrWhiteSpace(productHandle)) throw new ArgumentNullException(nameof(productHandle));

        var userSemaphore = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Idempotency guard: an existing local record for this user and plan wins
            // over creating a duplicate subscription.
            var existing = await FindForUserAsync(userId, cancellationToken);
            if (existing != null && existing.ProductHandle.Equals(productHandle, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Subscription already exists for user {UserId} on plan {ProductHandle}; returning existing subscription {SubscriptionId}.", userId, productHandle, existing.MaxioSubscriptionId);
                return await BuildStatusAsync(existing, cancellationToken);
            }

            var products = await _maxio.ListProductsForFamilyAsync(_productFamilyHandle, cancellationToken);
            var product = products.FirstOrDefault(p => p.Handle.Equals(productHandle, StringComparison.OrdinalIgnoreCase));
            if (product == null)
            {
                throw new SubscriptionPlanNotFoundException(productHandle, _productFamilyHandle);
            }

            var customer = await EnsureMaxioCustomerAsync(userId, userName, email, cancellationToken);

            var subscription = await _maxio.CreateSubscriptionAsync(customer.Id, product.Handle, cancellationToken);

            var record = new UserSubscription(userId, userName, customer.Id, subscription.Id,
                subscription.ProductHandle, subscription.ProductName, subscription.Currency,
                subscription.ProductPriceInCents, subscription.State,
                ParseDate(subscription.NextAssessmentAtUtc));
            await _subscriptionRepository.AddAsync(record, cancellationToken);

            _logger.LogInformation("User {UserId} subscribed to {ProductHandle}: Maxio subscription {SubscriptionId} (customer {CustomerId}).", userId, product.Handle, subscription.Id, customer.Id);

            return new SubscriptionStatus(subscription.Id, customer.Id, subscription.ProductHandle, subscription.ProductName,
                subscription.ProductPriceInCents, subscription.Currency, subscription.State,
                subscription.NextAssessmentAtUtc, subscription.CreatedAtUtc);
        }
        finally
        {
            userSemaphore.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionStatus>> GetMySubscriptionsAsync(string userId, string userName, CancellationToken cancellationToken = default)
    {
        var records = await FindAllForUserAsync(userId, cancellationToken);
        var statuses = new List<SubscriptionStatus>();
        foreach (var record in records)
        {
            statuses.Add(await BuildStatusAsync(record, cancellationToken));
        }
        return statuses;
    }

    private async Task<MaxioCustomer> EnsureMaxioCustomerAsync(string userId, string userName, string email, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (existing != null)
        {
            _logger.LogInformation("Maxio customer {CustomerId} already exists for user {UserId}.", existing.Id, userId);
            return existing;
        }

        var nameParts = (userName ?? string.Empty).Split('@');
        var firstName = string.IsNullOrWhiteSpace(nameParts[0]) ? "eShop" : nameParts[0];
        return await _maxio.CreateCustomerAsync(userId, email, firstName, "Shopper", cancellationToken);
    }

    private async Task<SubscriptionStatus> BuildStatusAsync(UserSubscription record, CancellationToken cancellationToken)
    {
        // Maxio is the system of record: refresh live state when reachable.
        try
        {
            var live = await _maxio.GetSubscriptionAsync(record.MaxioSubscriptionId, cancellationToken);
            if (live != null)
            {
                if (!string.Equals(live.State, record.State, StringComparison.OrdinalIgnoreCase)
                    || live.ProductPriceInCents != record.PriceInCents)
                {
                    record.SyncFromBillingSystem(live.State, live.ProductPriceInCents, live.Currency, ParseDate(live.NextAssessmentAtUtc));
                    await _subscriptionRepository.UpdateAsync(record);
                }

                return new SubscriptionStatus(record.MaxioSubscriptionId, record.MaxioCustomerId, record.ProductHandle,
                    live.ProductName, live.ProductPriceInCents, live.Currency, live.State,
                    live.NextAssessmentAtUtc, live.CreatedAtUtc ?? record.CreatedAtUtc.ToString("yyyy-MM-ddTHH:mm:sszzz"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not refresh Maxio subscription {record.MaxioSubscriptionId}; returning locally cached state. {ex.Message}");
        }

        return new SubscriptionStatus(record.MaxioSubscriptionId, record.MaxioCustomerId, record.ProductHandle,
            record.ProductName, record.PriceInCents, record.Currency, record.State,
            record.NextBillingDateUtc?.ToString("yyyy-MM-ddTHH:mm:sszzz"), record.CreatedAtUtc.ToString("yyyy-MM-ddTHH:mm:sszzz"));
    }

    private Task<UserSubscription?> FindForUserAsync(string userId, CancellationToken cancellationToken)
    {
        return _subscriptionRepository.FirstOrDefaultAsync(new UserSubscriptionsForUserSpec(userId), cancellationToken);
    }

    private async Task<IReadOnlyList<UserSubscription>> FindAllForUserAsync(string userId, CancellationToken cancellationToken)
    {
        return await _subscriptionRepository.ListAsync(new UserSubscriptionsForUserSpec(userId), cancellationToken);
    }

    private static DateTime? ParseDate(string? value)
    {
        return DateTime.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : null;
    }
}
