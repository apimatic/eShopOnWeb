using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates subscription billing with Maxio Advanced Billing, the billing
/// system of record.
///
/// Identity mapping (no local persistence required — Maxio holds it):
///   - The Maxio customer reference is "eshop-on-web:{userId}", so the eShopOnWeb
///     application user maps to exactly one Maxio customer. Maxio enforces one
///     customer per reference value.
///   - The Maxio subscription reference is "eshop-on-web:{userId}:{planHandle}",
///     so a double subscribe to the same plan resolves to the same subscription.
///
/// Concurrent duplicate calls are additionally serialized with per-key locks.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private const string ReferencePrefix = "eshop-on-web:";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CustomerLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly IMaxioApiClient _maxio;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly string _productFamilyHandle;

    public SubscriptionService(IMaxioApiClient maxio, IMemoryCache cache, IOptions<MaxioOptions> options, ILogger<SubscriptionService> logger)
    {
        _maxio = maxio;
        _cache = cache;
        _productFamilyHandle = options.Value.ProductFamilyHandle;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanSummary>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _cache.GetOrCreateAsync(CacheKey(), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return await ListPlansFromMaxioAsync(cancellationToken);
        });

        return plans!;
    }

    private string CacheKey() => $"maxio-plans:{_productFamilyHandle}";

    private async Task<IReadOnlyList<SubscriptionPlanSummary>> ListPlansFromMaxioAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListAllProductsAsync(cancellationToken);

        return products
            .Where(p => p.ProductFamily.Handle == _productFamilyHandle)
            .Where(p => p.ArchivedAt is null)
            .Select(p => new SubscriptionPlanSummary
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                HasTrial = p.TrialInterval is > 0,
                RequireCreditCard = p.RequireCreditCard ?? false,
                ProductFamilyHandle = p.ProductFamily.Handle
            })
            .ToList();
    }

    public async Task<SubscriptionSummary> SubscribeAsync(string userId, string userName, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => p.Handle == planHandle)
            ?? throw new PlanNotFoundException(planHandle);

        var reference = SubscriptionReference(userId, planHandle);
        var subscriptionLock = SubscribeLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Subscription {Reference} already exists in Maxio (id {SubscriptionId}); returning it.", reference, existing.Id);
                return MapSubscription(existing);
            }

            var customer = await EnsureCustomerAsync(userId, userName, email, cancellationToken);

            _logger.LogInformation("Creating Maxio subscription for customer {CustomerId} on plan {PlanHandle} (reference {Reference}).",
                customer.Id, planHandle, reference);

            var created = await _maxio.CreateSubscriptionAsync(planHandle, customer.Id, reference, cancellationToken);
            return MapSubscription(created);
        }
        finally
        {
            subscriptionLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> ListUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var customer = await _maxio.GetCustomerByReferenceAsync(CustomerReference(userId), cancellationToken);
        if (customer is null)
        {
            return new List<SubscriptionSummary>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    /// <summary>
    /// Returns the Maxio customer for the application user, creating it on first use.
    /// Idempotent: the Maxio customer reference is the user id, Maxio enforces one
    /// customer per reference, and a per-user lock serializes concurrent calls. If a
    /// concurrent creation slips through anyway, the duplicate-reference failure is
    /// resolved by re-reading the customer.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(string userId, string userName, string email, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(userId);
        var customerLock = CustomerLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await customerLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var (firstName, lastName) = SplitName(userName);

            try
            {
                return await _maxio.CreateCustomerAsync(reference, firstName, lastName, email, cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 422)
            {
                // Lost a race against a concurrent creation for the same reference —
                // read the winner back instead of failing the request.
                var created = await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);
                if (created is null)
                {
                    throw;
                }

                _logger.LogWarning("Maxio customer {Reference} was created concurrently; using the existing customer {CustomerId}.", reference, created.Id);
                return created;
            }
        }
        finally
        {
            customerLock.Release();
        }
    }

    private static string CustomerReference(string userId) => $"{ReferencePrefix}{userId}";

    private static string SubscriptionReference(string userId, string planHandle) => $"{ReferencePrefix}{userId}:{planHandle}";

    private static (string FirstName, string LastName) SplitName(string userName)
    {
        var name = userName?.Trim() ?? string.Empty;

        // Usernames are often plain email addresses (e.g. seeded users); use the
        // local part as the display name instead of the full address.
        if (name.Contains('@'))
        {
            name = name.Split('@')[0].Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Trim();
        }

        if (name.Length == 0)
        {
            return ("eShop", "Customer");
        }

        var separatorIndex = name.IndexOf(' ');
        if (separatorIndex < 0)
        {
            return (name, "Customer");
        }

        return (name[..separatorIndex], name[(separatorIndex + 1)..].Trim());
    }

    private static SubscriptionSummary MapSubscription(MaxioSubscription subscription)
    {
        return new SubscriptionSummary
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            State = subscription.State,
            CustomerId = subscription.Customer?.Id,
            ActivatedAt = subscription.ActivatedAt,
            NextBillingAt = subscription.CurrentPeriodEndsAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod
        };
    }
}
