using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Orchestrates the subscription flows against Maxio Advanced Billing.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan PlansCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CurrencyCacheDuration = TimeSpan.FromMinutes(60);

    private static readonly HashSet<string> LiveStates = new(StringComparer.Ordinal)
    {
        "active", "trialing", "assessing", "pending", "past_due", "soft_failure", "unpaid"
    };

    private readonly IMaxioApiClient _client;
    private readonly SubscriptionMappingDbContext _db;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _locks;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioApiClient client,
        SubscriptionMappingDbContext db,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        KeyedAsyncLock locks,
        ILogger<SubscriptionService> logger)
    {
        _client = client;
        _db = db;
        _options = options.Value;
        _cache = cache;
        _locks = locks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        string familyHandle = _options.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is required to list subscription plans.");
        }

        string cacheKey = $"maxio:plans:{familyHandle}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlanDto>? cached) && cached is not null)
        {
            return cached;
        }

        var products = await _client.ListFamilyProductsAsync(familyHandle, cancellationToken);
        var site = await GetSiteProfileAsync(cancellationToken);

        var plans = products.Select(p => ToPlanDto(p, CurrencyOf(site))).ToList();
        _cache.Set(cacheKey, plans, PlansCacheDuration);
        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(string appUserId, string email, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appUserId))
        {
            throw new ArgumentException("A valid user identity is required.", nameof(appUserId));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("A valid user email is required.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A subscription plan handle is required.", nameof(productHandle));
        }

        // Serialize subscribe per user so a double-click can never create two subscriptions.
        await using (await _locks.LockAsync($"subscribe:{appUserId}", cancellationToken))
        {
            var plans = await ListPlansAsync(cancellationToken);
            var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
                ?? throw new SubscriptionPlanNotFoundException(productHandle);

            var customer = await EnsureCustomerAsync(appUserId, email, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("User {UserId} already subscribed to plan {PlanHandle} (subscription {SubscriptionId}); returning existing.",
                    appUserId, plan.Handle, existing.Id);
                return new SubscribeResult
                {
                    Created = false,
                    Subscription = ToDto(existing, CurrencyOf(await GetSiteProfileAsync(cancellationToken)))
                };
            }

            var site = await GetSiteProfileAsync(cancellationToken);
            var created = await _client.CreateSubscriptionAsync(new MaxioSubscriptionCreateRequest
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = CollectionMethodFor(site)
            }, cancellationToken);

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}.",
                created.Id, appUserId, plan.Handle);

            return new SubscribeResult
            {
                Created = true,
                Subscription = ToDto(created, CurrencyOf(site))
            };
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string appUserId, string email, CancellationToken cancellationToken)
    {
        var customer = await TryFindCustomerAsync(appUserId, email, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var site = await GetSiteProfileAsync(cancellationToken);
        return subscriptions.Select(s => ToDto(s, CurrencyOf(site))).ToList();
    }

    // ------------------------------------------------------------------
    // Customer resolution (idempotent find-or-create)
    // ------------------------------------------------------------------

    private async Task<MaxioCustomer> EnsureCustomerAsync(string appUserId, string email, CancellationToken cancellationToken)
    {
        var existing = await TryFindCustomerAsync(appUserId, email, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(email);

        MaxioCustomer created;
        try
        {
            created = await _client.CreateCustomerAsync(new MaxioCustomerCreateRequest
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = email
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.IsDuplicate)
        {
            // Lost the race (or a previous attempt never persisted the local link): someone already
            // registered this reference. Re-read and use the existing Maxio customer.
            var raced = await _client.FindCustomerByReferenceAsync(email, cancellationToken);
            if (raced is not null)
            {
                await SaveCustomerLinkAsync(appUserId, raced, cancellationToken);
                return raced;
            }

            throw;
        }

        await SaveCustomerLinkAsync(appUserId, created, cancellationToken);
        return created;
    }

    private async Task<MaxioCustomer?> TryFindCustomerAsync(string appUserId, string email, CancellationToken cancellationToken)
    {
        var link = await _db.CustomerLinks.FirstOrDefaultAsync(l => l.AppUserId == appUserId, cancellationToken);
        if (link is not null)
        {
            return new MaxioCustomer { Id = link.MaxioCustomerId, Email = email, Reference = email };
        }

        var found = await _client.FindCustomerByReferenceAsync(email, cancellationToken);
        if (found is not null)
        {
            await SaveCustomerLinkAsync(appUserId, found, cancellationToken);
        }

        return found;
    }

    private async Task SaveCustomerLinkAsync(string appUserId, MaxioCustomer customer, CancellationToken cancellationToken)
    {
        if (await _db.CustomerLinks.AnyAsync(l => l.AppUserId == appUserId, cancellationToken))
        {
            return;
        }

        _db.CustomerLinks.Add(new MaxioCustomerLink
        {
            AppUserId = appUserId,
            MaxioCustomerId = customer.Id,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request in this process created the same link concurrently; the mapping is unchanged.
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions
            .Where(s => s.Product is not null
                        && string.Equals(s.Product.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
                        && LiveStates.Contains(s.State))
            .OrderByDescending(s => s.Id)
            .FirstOrDefault();
    }

    // ------------------------------------------------------------------
    // Mapping helpers
    // ------------------------------------------------------------------

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product, string? currency)
    {
        long priceInCents = product.PriceInCents ?? 0;
        return new SubscriptionPlanDto
        {
            Id = product.Id,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name,
            Description = product.Description,
            PriceInCents = priceInCents,
            Price = ToDecimal(priceInCents),
            CurrencyCode = currency,
            Interval = product.Interval ?? 1,
            IntervalUnit = string.IsNullOrEmpty(product.IntervalUnit) ? "month" : product.IntervalUnit!
        };
    }

    private static SubscriptionDto ToDto(MaxioSubscription subscription, string? currency)
    {
        long priceInCents = subscription.ProductPriceInCents
                            ?? subscription.Product?.PriceInCents
                            ?? 0;

        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id,
            State = subscription.State,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = priceInCents,
            Price = ToDecimal(priceInCents),
            CurrencyCode = currency,
            Interval = subscription.Product?.Interval ?? 1,
            IntervalUnit = string.IsNullOrEmpty(subscription.Product?.IntervalUnit) ? "month" : subscription.Product!.IntervalUnit!,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    private async Task<MaxioSite?> GetSiteProfileAsync(CancellationToken cancellationToken)
    {
        const string cacheKey = "maxio:site-profile";
        if (_cache.TryGetValue(cacheKey, out MaxioSite? site) && site is not null)
        {
            return site;
        }

        site = await _client.GetSiteAsync(cancellationToken);
        if (site is not null)
        {
            _cache.Set(cacheKey, site, CurrencyCacheDuration);
        }

        return site;
    }

    private static string? CurrencyOf(MaxioSite? site)
        => string.IsNullOrWhiteSpace(site?.Currency) ? null : site.Currency;

    private static string CollectionMethodFor(MaxioSite? site)
    {
        // Subscribing without a stored card ("payment method not required"): bill by invoice.
        // Relationship invoicing sites use "remittance"; legacy statements sites use "invoice".
        return site?.RelationshipInvoicingEnabled == true ? "remittance" : "invoice";
    }

    private static decimal ToDecimal(long cents) => Math.Round(cents / 100m, 2, MidpointRounding.AwayFromZero);

    private static (string FirstName, string LastName) SplitName(string email)
    {
        // The identity system only stores an email, so derive a best-effort display name from it.
        // Maxio rejects blank last names, so the fallbacks below are always non-empty.
        string localPart = email.Split('@')[0];
        int dotIndex = localPart.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex > 0)
        {
            return (localPart[..dotIndex], localPart[(dotIndex + 1)..]);
        }

        return (localPart, "Customer");
    }
}
