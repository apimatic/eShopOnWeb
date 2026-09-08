using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data.Billing;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks =
        new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> TerminalStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly IMaxioApiClient _maxio;
    private readonly MaxioOptions _options;
    private readonly BillingDbContext _billingDbContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioApiClient maxio,
        IOptions<MaxioOptions> options,
        BillingDbContext billingDbContext,
        IMemoryCache cache,
        ILogger<SubscriptionService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _billingDbContext = billingDbContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        string cacheKey = $"maxio-plans:{_options.ProductFamilyHandle}";

        var cached = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            var products = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);

            return products
                .Where(p => p.ArchivedAt is null)
                .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
                .OrderBy(p => p.Id)
                .Select(ToPlanDto)
                .ToList();
        });

        return cached ?? new List<SubscriptionPlanDto>();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        string appUserId,
        string email,
        string planHandle,
        string? firstName,
        string? lastName,
        CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var subscriptionLock = SubscriptionLocks.GetOrAdd(appUserId, _ => new SemaphoreSlim(1, 1));

        await subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            var account = await GetOrCreateAccountAsync(appUserId, email, firstName, lastName, createIfMissing: true, cancellationToken)
                ?? throw new BillingAccountNotFoundException();

            var existing = await FindExistingSubscriptionAsync(account, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                await LinkSubscriptionAsync(account, existing, cancellationToken);
                return new SubscribeResult { Subscription = ToDto(existing), IsNew = false };
            }

            var nextBillingAt = ComputeNextBillingAt(plan.Interval, plan.IntervalUnit);

            MaxioSubscription created;
            try
            {
                created = await _maxio.CreateSubscriptionAsync(new MaxioSubscriptionWrite
                {
                    ProductHandle = plan.Handle,
                    CustomerReference = account.MaxioReference,
                    NextBillingAt = nextBillingAt
                }, cancellationToken);
            }
            catch (MaxioApiException)
            {
                var recheck = await FindExistingSubscriptionAsync(account, plan.Handle, cancellationToken);
                if (recheck is not null)
                {
                    await LinkSubscriptionAsync(account, recheck, cancellationToken);
                    return new SubscribeResult { Subscription = ToDto(recheck), IsNew = false };
                }

                throw;
            }

            await LinkSubscriptionAsync(account, created, cancellationToken);

            return new SubscribeResult { Subscription = ToDto(created), IsNew = true };
        }
        finally
        {
            subscriptionLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string appUserId, string email, CancellationToken cancellationToken)
    {
        var account = await GetOrCreateAccountAsync(appUserId, email, firstName: null, lastName: null, createIfMissing: false, cancellationToken);
        if (account?.MaxioCustomerId is not long customerId)
        {
            return new List<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        foreach (var subscription in subscriptions)
        {
            await LinkSubscriptionAsync(account, subscription, cancellationToken);
        }

        return subscriptions
            .OrderByDescending(s => s.Id)
            .Select(ToDto)
            .ToList();
    }

    private async Task<BillingAccount?> GetOrCreateAccountAsync(
        string appUserId,
        string email,
        string? firstName,
        string? lastName,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        var account = await _billingDbContext.BillingAccounts
            .FirstOrDefaultAsync(a => a.AppUserId == appUserId, cancellationToken);

        if (account?.MaxioCustomerId is not null)
        {
            return account;
        }

        string reference = BuildReference(email);
        MaxioCustomer? maxioCustomer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);

        if (maxioCustomer is null && createIfMissing)
        {
            var (first, last) = ResolveNames(email, firstName, lastName);

            try
            {
                maxioCustomer = await _maxio.CreateCustomerAsync(new MaxioCustomerWrite
                {
                    FirstName = first,
                    LastName = last,
                    Email = email,
                    Reference = reference
                }, cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity || ex.StatusCode == HttpStatusCode.Conflict)
            {
                maxioCustomer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            }
        }

        if (maxioCustomer is null)
        {
            return account;
        }

        var (resolvedFirstName, resolvedLastName) = ResolveNames(email, maxioCustomer.FirstName ?? firstName, maxioCustomer.LastName ?? lastName);

        if (account is not null)
        {
            account.MaxioReference = reference;
            account.MaxioCustomerId = maxioCustomer.Id;
            account.UpdatedUtc = DateTime.UtcNow;
            await _billingDbContext.SaveChangesAsync(cancellationToken);
            return account;
        }

        account = new BillingAccount
        {
            AppUserId = appUserId,
            AppUserName = email,
            Email = email,
            MaxioReference = reference,
            MaxioCustomerId = maxioCustomer.Id,
            FirstName = resolvedFirstName,
            LastName = resolvedLastName,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        _billingDbContext.BillingAccounts.Add(account);
        await _billingDbContext.SaveChangesAsync(cancellationToken);

        return account;
    }

    private async Task<MaxioSubscription?> FindExistingSubscriptionAsync(BillingAccount account, string productHandle, CancellationToken cancellationToken)
    {
        if (account.MaxioCustomerId is not long customerId)
        {
            return null;
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions
            .Where(s => string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
            .Where(s => s.State is null || !TerminalStates.Contains(s.State))
            .OrderByDescending(s => s.Id)
            .FirstOrDefault();
    }

    private async Task LinkSubscriptionAsync(BillingAccount account, MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        if (subscription.Id is not long maxioSubscriptionId)
        {
            return;
        }

        bool alreadyLinked = await _billingDbContext.BillingSubscriptions
            .AnyAsync(s => s.MaxioSubscriptionId == maxioSubscriptionId, cancellationToken);

        if (!alreadyLinked)
        {
            _billingDbContext.BillingSubscriptions.Add(new BillingSubscription
            {
                BillingAccountId = account.Id,
                MaxioSubscriptionId = maxioSubscriptionId,
                ProductHandle = subscription.Product?.Handle ?? string.Empty,
                CreatedUtc = DateTime.UtcNow
            });

            await _billingDbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static string BuildReference(string email)
    {
        return $"eshop-{email.Trim().ToLowerInvariant()}";
    }

    private static (string FirstName, string LastName) ResolveNames(string email, string? firstName, string? lastName)
    {
        if (!string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(lastName))
        {
            return (firstName.Trim(), lastName.Trim());
        }

        string localPart = email.Split('@')[0];
        var parts = localPart.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length >= 2)
        {
            return (Capitalize(parts[0]), Capitalize(parts[^1]));
        }

        return (Capitalize(localPart), "User");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    private static DateTimeOffset ComputeNextBillingAt(int interval, string intervalUnit)
    {
        int months = string.Equals(intervalUnit, "day", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        int days = string.Equals(intervalUnit, "day", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        return DateTimeOffset.UtcNow.AddMonths(months * Math.Max(1, interval)).AddDays(days * Math.Max(1, interval));
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product)
    {
        return new SubscriptionPlanDto
        {
            Id = product.Id ?? 0,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            Price = ToPrice(product.PriceInCents),
            Interval = product.Interval ?? 1,
            IntervalUnit = string.IsNullOrWhiteSpace(product.IntervalUnit) ? "month" : product.IntervalUnit,
            RequireCreditCard = product.RequireCreditCard,
            Taxable = product.Taxable
        };
    }

    private static SubscriptionDto ToDto(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            State = subscription.State ?? "unknown",
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            Price = ToPrice(subscription.ProductPriceInCents),
            Currency = subscription.Currency,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            Reference = subscription.Reference
        };
    }

    private static decimal ToPrice(long? cents)
    {
        return cents.HasValue ? cents.Value / 100m : 0m;
    }
}
