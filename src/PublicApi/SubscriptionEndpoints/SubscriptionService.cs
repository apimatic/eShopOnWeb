using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Orchestrates the subscription billing capability against Maxio Advanced Billing,
/// which is the billing system of record. Customer linkage is idempotent: the eShopOnWeb
/// user Id is stored as the Maxio customer reference, which Maxio enforces as unique.
/// </summary>
public class SubscriptionService
{
    // End-of-life states per the spec's Subscription-State enum; a subscription in any other
    // state is considered live and blocks creating a duplicate for the same plan.
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "on_hold", "suspended", "trial_ended", "unpaid"
    };

    private const string ProductFamilyIdCacheKey = "Maxio:ProductFamilyId";

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioClient maxioClient,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        IAppLogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await GetProductFamilyIdAsync(cancellationToken);
        var products = await _maxioClient.ListProductsForFamilyAsync(familyId, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .OrderBy(p => p.PriceInCents)
            .Select(p => new SubscriptionPlanDto
            {
                ProductId = p.Id,
                Handle = p.Handle ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                RequiresPaymentMethod = p.RequireCreditCard
            })
            .ToList();
    }

    /// <summary>
    /// Idempotently enrolls the user in the given plan. If the user already has a live
    /// subscription for the plan, that subscription is returned with Created = false.
    /// Returns null when the handle does not match any plan in the configured family.
    /// </summary>
    public async Task<SubscribeResult?> SubscribeAsync(ApplicationUser user, string productHandle, CancellationToken cancellationToken = default)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            return null;
        }

        var customer = await EnsureCustomerAsync(user, cancellationToken);

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
            !EndOfLifeStates.Contains(s.State ?? string.Empty));
        if (existing != null)
        {
            _logger.LogInformation(
                "User {UserId} already has a live subscription {SubscriptionId} for plan {PlanHandle}; returning it instead of creating a duplicate.",
                user.Id, existing.Id, plan.Handle);
            return new SubscribeResult(ToDto(existing), Created: false);
        }

        // Subscription references must be unique in Maxio, so scope it to user + plan
        // (the customer reference alone is the user Id and is already taken by the first plan).
        var created = await _maxioClient.CreateSubscriptionAsync(plan.Handle, customer.Id, $"{user.Id}:{plan.Handle}", cancellationToken);
        _logger.LogInformation("Created subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}.", created.Id, user.Id, plan.Handle);
        return new SubscribeResult(ToDto(created), Created: true);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToDto).ToList();
    }

    /// <summary>
    /// Returns the Maxio customer for the user, creating it on first use. The Maxio customer
    /// reference is the eShopOnWeb user Id; Maxio enforces reference uniqueness, so a racing
    /// duplicate create (422) is resolved by re-reading the customer that won the race.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(user);
        try
        {
            return await _maxioClient.CreateCustomerAsync(firstName, lastName, user.Email ?? user.UserName ?? user.Id, user.Id, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var winner = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
            if (winner != null)
            {
                return winner;
            }
            throw;
        }
    }

    private async Task<long> GetProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(ProductFamilyIdCacheKey, out long cachedId))
        {
            return cachedId;
        }

        var handle = _settings.ProductFamilyHandle;
        var families = await _maxioClient.ListProductFamiliesAsync(cancellationToken);
        var family = families.FirstOrDefault(f => string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase));
        if (family is null)
        {
            throw new InvalidOperationException(
                $"No Maxio product family found with handle '{handle}'. Check the Maxio:ProductFamilyHandle configuration value.");
        }

        _cache.Set(ProductFamilyIdCacheKey, family.Id, TimeSpan.FromMinutes(10));
        return family.Id;
    }

    private static (string FirstName, string LastName) DeriveName(ApplicationUser user)
    {
        var localPart = (user.Email ?? user.UserName ?? "shopper").Split('@')[0];
        var parts = localPart.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.Length > 0 ? parts[0] : "Shopper";
        var lastName = parts.Length > 1 ? parts[^1] : firstName;
        return (firstName, lastName);
    }

    private static SubscriptionDto ToDto(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference
    };
}

public record SubscribeResult(SubscriptionDto Subscription, bool Created);
