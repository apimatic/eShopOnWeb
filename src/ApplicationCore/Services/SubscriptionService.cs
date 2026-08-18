using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserGates = new(StringComparer.Ordinal);

    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "assessing",
        "pending",
        "trialing",
        "past_due",
        "soft_failure",
        "unpaid",
        "paused",
        "awaiting_signup"
    };

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly ISubscriptionBillingSettings _settings;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioAdvancedBillingClient maxio,
        ISubscriptionBillingSettings settings,
        IAppLogger<SubscriptionService> logger)
    {
        _maxio = maxio;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = RequireProductFamilyHandle();
        var products = await _maxio.ListProductsForFamilyAsync(familyHandle, cancellationToken);
        return products
            .Where(plan => !string.IsNullOrWhiteSpace(plan.Handle))
            .ToList();
    }

    public async Task<ShopperSubscription> SubscribeAsync(SubscribeShopperRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request, nameof(request));
        Guard.Against.NullOrEmpty(request.UserId, nameof(request.UserId));
        Guard.Against.NullOrEmpty(request.Email, nameof(request.Email));

        var gate = UserGates.GetOrAdd(request.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var productHandle = await ResolveProductHandleAsync(request.ProductHandle, cancellationToken);
            var customer = await EnsureCustomerAsync(request, cancellationToken);
            var existingForPlan = await FindLiveSubscriptionForPlanAsync(customer.Id, productHandle, cancellationToken);
            if (existingForPlan is not null)
            {
                _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for user {UserId} and plan {ProductHandle}.",
                    existingForPlan.Id, request.UserId, productHandle);
                return existingForPlan;
            }

            var subscriptionReference = BuildSubscriptionReference(request.UserId, productHandle);
            var existingByReference = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existingByReference is not null && IsLive(existingByReference.State))
            {
                return existingByReference;
            }

            if (existingByReference is not null)
            {
                subscriptionReference = $"{subscriptionReference}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(new CreateBillingSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customer.Id,
                    Reference = subscriptionReference
                }, cancellationToken);

                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} on plan {ProductHandle}.",
                    created.Id, request.UserId, productHandle);
                return created;
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 422)
            {
                var recovered = await FindLiveSubscriptionForPlanAsync(customer.Id, productHandle, cancellationToken)
                    ?? await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (recovered is not null)
                {
                    _logger.LogInformation("Recovered existing Maxio subscription after a 422 for user {UserId} and plan {ProductHandle}.",
                        request.UserId, productHandle);
                    return recovered;
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));

        var customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(subscription => subscription.Id)
            .ToList();
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(SubscribeShopperRequest request, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(request.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(request.UserName, request.Email);
        try
        {
            var created = await _maxio.CreateCustomerAsync(new CreateBillingCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = request.Email,
                Reference = request.UserId
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Id, request.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            var recovered = await _maxio.FindCustomerByReferenceAsync(request.UserId, cancellationToken);
            if (recovered is not null)
            {
                _logger.LogInformation("Recovered existing Maxio customer after a 422 for user {UserId}.", request.UserId);
                return recovered;
            }

            throw;
        }
    }

    private async Task<string> ResolveProductHandleAsync(string? productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        if (plans.Count == 0)
        {
            throw new SubscriptionException("No subscription plans are available.", 503);
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            return plans[0].Handle;
        }

        var match = plans.FirstOrDefault(plan =>
            string.Equals(plan.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new SubscriptionException($"Unknown subscription plan '{productHandle}'.", 400);
        }

        return match.Handle;
    }

    private async Task<ShopperSubscription?> FindLiveSubscriptionForPlanAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription =>
            IsLive(subscription.State) &&
            string.Equals(subscription.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private string RequireProductFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is not configured.");
        }

        return _settings.ProductFamilyHandle.Trim();
    }

    internal static string BuildSubscriptionReference(string userId, string productHandle)
        => $"{userId}:{productHandle}";

    internal static bool IsLive(string? state)
        => !string.IsNullOrWhiteSpace(state) && LiveStates.Contains(state);

    internal static (string FirstName, string LastName) SplitDisplayName(string? userName, string email)
    {
        var source = !string.IsNullOrWhiteSpace(userName) ? userName : email;
        var local = source.Contains('@', StringComparison.Ordinal)
            ? source[..source.IndexOf('@')]
            : source;
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (Capitalize(parts[0]), Capitalize(parts[1]));
        }

        var first = parts.Length == 1 ? Capitalize(parts[0]) : "Shopper";
        return (first, "Subscriber");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Shopper";
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 1)
        {
            return trimmed.ToUpperInvariant();
        }

        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..].ToLowerInvariant();
    }
}
