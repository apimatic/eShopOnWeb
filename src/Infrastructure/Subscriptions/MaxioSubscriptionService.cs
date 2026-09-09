using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

/// <summary>
/// Orchestrates enrollment: guarantees an idempotent Maxio customer per
/// eShopOnWeb user, creates subscriptions in Maxio (the billing system of
/// record) and mirrors them into the local mapping store.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Maxio subscription states that mean the subscriber still holds the plan.
    // A string array (not HashSet) so EF Core can translate Contains() to SQL.
    private static readonly string[] LiveStates =
    {
        "active", "trialing", "assessing", "pending", "soft_failure",
        "past_due", "unpaid", "on_hold", "suspended"
    };

    private static bool IsLiveState(string state)
        => LiveStates.Contains(state, StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserGates = new(StringComparer.Ordinal);

    private readonly IMaxioClient _maxio;
    private readonly SubscriptionsDbContext _db;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioClient maxio,
        SubscriptionsDbContext db,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _maxio = maxio;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured())
        {
            throw new InvalidOperationException(
                "The Maxio integration is not configured. Set the Maxio:ApiKey, Maxio:Subdomain " +
                "(or Maxio:BaseUrl) and Maxio:ProductFamilyHandle settings, e.g. via user-secrets or " +
                "the MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN and MAXIO_DEFAULT_PRODUCT_FAMILY environment variables.");
        }
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var products = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle!,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                RequireCreditCard = p.RequireCreditCard
            })
            .ToList();
    }

    public async Task<SubscriptionInfo> SubscribeAsync(ApplicationUser user, string? planHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var plans = await GetPlansAsync(cancellationToken);
        SubscriptionPlan plan;
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            plan = plans.FirstOrDefault()
                ?? throw new MaxioApiException(404, $"No subscription plans found in product family '{_options.ProductFamilyHandle}'.", new[] { "product_family_empty" });
        }
        else
        {
            plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
                ?? throw new MaxioApiException(404, $"Unknown subscription plan '{planHandle}'.", new[] { "plan_not_found" });
        }

        var gate = UserGates.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // 1. Local check: an existing live record for the same user + plan.
            var local = await _db.SubscriptionRecords
                .Where(r => r.UserId == user.Id && r.PlanHandle == plan.Handle && LiveStates.Contains(r.State))
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (local is not null)
            {
                return ToInfo(local, live: false, alreadySubscribed: true);
            }

            // 2. Idempotently ensure the Maxio customer.
            var customer = await EnsureCustomerAsync(user, cancellationToken);

            // 3. Remote check: Maxio is the system of record, so also look for a
            //    live subscription there (protects against a reset local store).
            var remoteSubscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var remote = remoteSubscriptions.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase)
                && IsLiveState(s.State));
            if (remote is not null)
            {
                var existingRecord = await UpsertRecordAsync(user, customer.Id, remote, cancellationToken);
                return ToInfo(existingRecord, live: true, alreadySubscribed: true, nextBillingAt: remote.CurrentPeriodEndsAt);
            }

            // 4. Create the subscription in Maxio.
            var created = await _maxio.CreateSubscriptionAsync(plan.Handle, customer.Id, Guid.NewGuid().ToString("N"), cancellationToken);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}",
                created.Id, user.Id, plan.Handle);

            var record = await UpsertRecordAsync(user, customer.Id, created, cancellationToken);
            return ToInfo(record, live: true, alreadySubscribed: false, nextBillingAt: created.CurrentPeriodEndsAt);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var records = await _db.SubscriptionRecords
            .Where(r => r.UserId == user.Id)
            .OrderByDescending(r => r.Id)
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
        {
            return Array.Empty<SubscriptionInfo>();
        }

        // Best-effort live refresh from Maxio; fall back to cached values.
        Dictionary<int, MaxioSubscription> remoteById = new();
        try
        {
            var customerId = records[0].MaxioCustomerId;
            var remoteSubscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
            remoteById = remoteSubscriptions.ToDictionary(s => s.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not refresh live subscription state from Maxio; serving cached values.");
        }

        return records.Select(r =>
        {
            if (remoteById.TryGetValue(r.MaxioSubscriptionId, out var remote))
            {
                r.State = remote.State;
                r.PriceInCents = remote.ProductPriceInCents ?? remote.Product?.PriceInCents ?? r.PriceInCents;
                r.UpdatedAtUtc = DateTime.UtcNow;
                return ToInfo(r, live: true, alreadySubscribed: false, nextBillingAt: remote.CurrentPeriodEndsAt);
            }
            return ToInfo(r, live: false, alreadySubscribed: false);
        }).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = GetCustomerReference(user);

        var existing = await _maxio.LookupCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : user.UserName!;
        var input = new CreateCustomerInput
        {
            FirstName = "eShop",
            LastName = "Subscriber",
            Email = email,
            Organization = "eShopOnWeb",
            Reference = reference
        };

        try
        {
            return await _maxio.CreateCustomerAsync(input, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Concurrent creation won the race; Maxio enforces one customer per
            // reference value. Re-look up instead of failing.
            var racedCustomer = await _maxio.LookupCustomerByReferenceAsync(reference, cancellationToken);
            if (racedCustomer is not null)
            {
                return racedCustomer;
            }
            throw;
        }
    }

    private static string GetCustomerReference(ApplicationUser user)
        => $"eshopweb-user-{user.Id}";

    private async Task<SubscriptionRecord> UpsertRecordAsync(ApplicationUser user, int maxioCustomerId, MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        var record = await _db.SubscriptionRecords
            .FirstOrDefaultAsync(r => r.MaxioSubscriptionId == subscription.Id, cancellationToken);

        if (record is null)
        {
            record = new SubscriptionRecord
            {
                UserId = user.Id,
                UserEmail = user.Email ?? user.UserName ?? string.Empty,
                MaxioCustomerId = maxioCustomerId,
                MaxioSubscriptionId = subscription.Id,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.SubscriptionRecords.Add(record);
        }

        record.PlanHandle = subscription.Product?.Handle ?? string.Empty;
        record.PlanName = subscription.Product?.Name ?? string.Empty;
        record.State = subscription.State;
        record.PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0;
        record.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return record;
    }

    private static SubscriptionInfo ToInfo(
        SubscriptionRecord record,
        bool live,
        bool alreadySubscribed,
        DateTime? nextBillingAt = null)
        => new()
        {
            MaxioSubscriptionId = record.MaxioSubscriptionId,
            PlanHandle = record.PlanHandle,
            PlanName = record.PlanName,
            PriceInCents = record.PriceInCents,
            State = record.State,
            NextBillingAt = nextBillingAt,
            ActivatedAt = record.CreatedAtUtc,
            CreatedAtUtc = record.CreatedAtUtc,
            Live = live,
            AlreadySubscribed = alreadySubscribed
        };
}
