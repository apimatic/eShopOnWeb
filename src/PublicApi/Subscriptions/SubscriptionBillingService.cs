using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Orchestrates the subscription hero flow against Maxio Advanced Billing:
/// ensures a Maxio customer exists for the eShopOnWeb user (idempotent),
/// enrolls them in a plan (idempotent per plan), and reads back their
/// subscriptions.
/// </summary>
public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan _plansCacheDuration = TimeSpan.FromSeconds(60);
    private const string PlansCacheKey = "maxio:subscription-plans";

    // Maxio subscription states in which the shopper still holds the plan.
    private static readonly HashSet<string> _liveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "past_due", "unpaid", "on_hold"
    };

    // Serializes subscribe calls per user within this process so a double-click
    // can never race past the existing-subscription check.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeLocks = new();

    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMemoryCache _memoryCache;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioClient maxioClient,
        UserManager<ApplicationUser> userManager,
        IMemoryCache memoryCache,
        IOptions<MaxioSettings> settings,
        ILogger<SubscriptionBillingService> logger)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
        _memoryCache = memoryCache;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _memoryCache.GetOrCreateAsync(PlansCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _plansCacheDuration;
            return await _maxioClient.ListProductsAsync(_settings.ProductFamilyHandle, cancellationToken);
        });

        return (products ?? new List<MaxioProduct>())
            .Where(p => p.ArchivedAt == null)
            .Select(MapPlan)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(ClaimsPrincipal principal, string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new PlanNotFoundException(productHandle ?? string.Empty);
        }

        var user = await ResolveUserAsync(principal);

        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PlanNotFoundException(productHandle);
        }

        var customer = await EnsureCustomerAsync(user, cancellationToken);

        var userLock = _subscribeLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var current = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase)
                && s.State != null
                && _liveStates.Contains(s.State));

            if (current != null)
            {
                _logger.LogInformation("User {UserId} already holds subscription {SubscriptionId} for plan {PlanHandle}; returning it instead of creating a duplicate.",
                    user.Id, current.Id, productHandle);
                return new SubscribeResult(MapSubscription(current), Created: false);
            }

            var created = await _maxioClient.CreateSubscriptionAsync(productHandle, user.Id, cancellationToken);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}.",
                created.Id, user.Id, productHandle);
            return new SubscribeResult(MapSubscription(created), Created: true);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(principal);

        var customer = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer == null)
        {
            return new List<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(user.Email ?? user.UserName ?? user.Id);

        try
        {
            return await _maxioClient.CreateCustomerAsync(user.Email ?? user.UserName ?? string.Empty, firstName, lastName, user.Id, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio enforces uniqueness of customer.reference; a concurrent request may
            // have created the customer between our lookup and create. Re-read it.
            var concurrent = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
            if (concurrent != null)
            {
                return concurrent;
            }

            throw;
        }
    }

    private async Task<ApplicationUser> ResolveUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.Identity?.Name
            ?? principal.FindFirst(ClaimTypes.Name)?.Value
            ?? throw new UnauthorizedAccessException("The token does not contain a username claim.");

        var user = await _userManager.FindByNameAsync(username);
        return user ?? throw new UnauthorizedAccessException($"No eShopOnWeb user exists for '{username}'.");
    }

    private static (string FirstName, string LastName) DeriveName(string emailOrUsername)
    {
        var localPart = emailOrUsername.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (Capitalize(parts[0]), Capitalize(parts[1]));
        }

        return (Capitalize(localPart), "Customer");
    }

    private static string Capitalize(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "Customer"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());

    private static SubscriptionPlanDto MapPlan(MaxioProduct product) => new SubscriptionPlanDto
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description ?? string.Empty,
        PriceInCents = product.PriceInCents,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription) => new SubscriptionDto
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        Price = (subscription.Product?.PriceInCents ?? 0) / 100m,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        ActivatedAt = subscription.ActivatedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        CanceledAt = subscription.CanceledAt
    };
}
