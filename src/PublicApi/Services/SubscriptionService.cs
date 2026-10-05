using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

/// <summary>
/// The subscription plan as exposed by the API.
/// </summary>
public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string message) : base(message) { }
}

public interface ISubscriptionService
{
    Task<ApplicationUser?> ResolveUserAsync(ClaimsPrincipal principal, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default);
    Task<(SubscriptionDto Subscription, bool AlreadySubscribed)> SubscribeAsync(
        ApplicationUser user, string? planHandle, int? planId, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken ct = default);
}

/// <summary>
/// Orchestrates subscriptions with Maxio Advanced Billing as the system of
/// record. The eShopOnWeb user id is stored as the Maxio customer reference,
/// so no local persistence is required and lookups are idempotent.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    // Subscription states that mean "the shopper already holds this plan".
    private static readonly HashSet<string> ActiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "trial_ended", "pending", "past_due", "on_hold", "paused", "unpaid"
    };

    private const string PlansCacheKey = "maxio:plans:{0}";
    private static readonly TimeSpan PlansCacheLifetime = TimeSpan.FromMinutes(2);

    // In-process, per-user lock so a double-click cannot create two
    // subscriptions concurrently (Maxio itself allows duplicate subscribes).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly IMaxioClient _maxio;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMemoryCache _cache;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioClient maxio,
        UserManager<ApplicationUser> userManager,
        IMemoryCache cache,
        IOptions<MaxioSettings> settings,
        ILogger<SubscriptionService> logger)
    {
        _maxio = maxio;
        _userManager = userManager;
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ApplicationUser?> ResolveUserAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var username = principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(username);
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        var plans = await _cache.GetOrCreateAsync(string.Format(PlansCacheKey, _settings.ProductFamilyHandle), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PlansCacheLifetime;
            var products = await _maxio.ListFamilyProductsAsync(_settings.ProductFamilyHandle, ct);
            return (IReadOnlyList<MaxioProduct>)products
                .Where(p => p.ArchivedAt == null)
                .OrderBy(p => p.PriceInCents)
                .ToList();
        });

        return plans.Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Handle = p.Handle ?? string.Empty,
            Name = p.Name ?? string.Empty,
            Description = p.Description,
            PriceInCents = p.PriceInCents,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit ?? string.Empty
        }).ToList();
    }

    public async Task<(SubscriptionDto Subscription, bool AlreadySubscribed)> SubscribeAsync(
        ApplicationUser user, string? planHandle, int? planId, CancellationToken ct = default)
    {
        var plan = (await ListPlansAsync(ct)).FirstOrDefault(p =>
            (planHandle != null && string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)) ||
            (planId.HasValue && p.Id == planId.Value));

        if (plan == null)
        {
            throw new PlanNotFoundException($"Subscription plan '{planHandle ?? planId?.ToString()}' was not found.");
        }

        var customer = await EnsureCustomerAsync(user, ct);

        var userLock = UserLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(ct);
        try
        {
            var existing = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, ct);
            var duplicate = existing.FirstOrDefault(s =>
                s.Product?.Handle == plan.Handle &&
                ActiveStates.Contains(s.State ?? string.Empty));

            if (duplicate != null)
            {
                _logger.LogInformation("User {UserId} is already subscribed to plan {PlanHandle} (subscription {SubscriptionId}).",
                    user.Id, plan.Handle, duplicate.Id);
                return (MapSubscription(duplicate), true);
            }

            var created = await _maxio.CreateSubscriptionAsync(customer.Id, plan.Handle, _settings.PaymentCollectionMethod, ct);
            _logger.LogInformation("User {UserId} subscribed to plan {PlanHandle} (subscription {SubscriptionId}).",
                user.Id, plan.Handle, created.Id);
            return (MapSubscription(created), false);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken ct = default)
    {
        var customer = await _maxio.GetCustomerByReferenceAsync(user.Id, ct);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, ct);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(MapSubscription)
            .ToList();
    }

    /// <summary>
    /// Ensures exactly one Maxio customer exists for the eShopOnWeb user,
    /// keyed by the user id as the customer reference. Safe to call repeatedly.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken ct)
    {
        var reference = user.Id;
        var existing = await _maxio.GetCustomerByReferenceAsync(reference, ct);
        if (existing != null)
        {
            return existing;
        }

        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email! : user.UserName!;
        var firstName = email.Split('@')[0];
        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = "eShop";
        }

        try
        {
            return await _maxio.CreateCustomerAsync(firstName, "Customer", email, reference, ct);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // A concurrent request may have created the customer between our
            // lookup and our create; references are unique in Maxio.
            _logger.LogWarning("Customer create for reference {Reference} was rejected (422); retrying lookup.", reference);
            var raced = await _maxio.GetCustomerByReferenceAsync(reference, ct);
            if (raced != null)
            {
                return raced;
            }

            throw;
        }
    }

    private static SubscriptionDto MapSubscription(MaxioSubscription s)
    {
        return new SubscriptionDto
        {
            SubscriptionId = s.Id,
            PlanHandle = s.Product?.Handle ?? string.Empty,
            PlanName = s.Product?.Name ?? string.Empty,
            PriceInCents = s.ProductPriceInCents > 0
                ? s.ProductPriceInCents
                : s.Product?.PriceInCents ?? 0,
            Currency = s.Currency,
            State = s.State ?? string.Empty,
            NextBillingAt = s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            CreatedAt = s.CreatedAt,
            CanceledAt = s.CanceledAt,
            PaymentCollectionMethod = s.PaymentCollectionMethod
        };
    }
}