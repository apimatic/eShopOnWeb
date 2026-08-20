using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "trialing",
        "assessing",
        "active",
        "soft_failure",
        "past_due",
        "unpaid",
        "paused",
        "awaiting_signup"
    };

    private readonly IMaxioApiClient _maxio;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();

    public MaxioSubscriptionBillingService(
        IMaxioApiClient maxio,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var products = await _maxio.ListProductsForFamilyAsync(_options.Value.ProductFamilyHandle, cancellationToken);
        var plans = new List<SubscriptionPlan>();
        foreach (var product in products)
        {
            if (product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
            {
                continue;
            }

            plans.Add(MaxioMappings.ToPlan(product));
        }

        return plans;
    }

    public async Task<ShopperSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (shopper is null)
        {
            throw new ArgumentNullException(nameof(shopper));
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException(400, "productHandle is required.");
        }

        productHandle = productHandle.Trim();
        await EnsureProductIsInFamilyAsync(productHandle, cancellationToken);

        var gate = _userLocks.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id, shopper.UserId, productHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for user {UserId} and plan {ProductHandle}.",
                    existing.Id, shopper.UserId, productHandle);
                return MaxioMappings.ToShopperSubscription(existing);
            }

            var subscriptionReference = BuildSubscriptionReference(shopper.UserId, productHandle);
            var byReference = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (byReference is not null && IsLive(byReference))
            {
                return MaxioMappings.ToShopperSubscription(byReference);
            }

            if (byReference is not null)
            {
                subscriptionReference = $"{subscriptionReference}:{Guid.NewGuid():N}";
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(
                    new MaxioCreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customer.Id,
                        Reference = subscriptionReference,
                        PaymentCollectionMethod = "remittance"
                    },
                    uniquenessToken: Guid.NewGuid().ToString(),
                    cancellationToken);

                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} for user {UserId} on plan {ProductHandle}.",
                    created.Id, shopper.UserId, productHandle);
                return MaxioMappings.ToShopperSubscription(created);
            }
            catch (BillingException ex) when (ex.StatusCode is 409 or 400)
            {
                var recovered = await FindLiveSubscriptionAsync(customer.Id, shopper.UserId, productHandle, cancellationToken);
                if (recovered is not null)
                {
                    return MaxioMappings.ToShopperSubscription(recovered);
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new BillingException(401, "The caller's identity is missing from the token.");
        }

        var customer = await _maxio.GetCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var result = new List<ShopperSubscription>(subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            result.Add(MaxioMappings.ToShopperSubscription(subscription));
        }

        return result;
    }

    private async Task EnsureProductIsInFamilyAsync(string productHandle, CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsForFamilyAsync(_options.Value.ProductFamilyHandle, cancellationToken);
        foreach (var product in products)
        {
            if (product.ArchivedAt is null
                && string.Equals(product.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        throw new BillingException(400,
            $"Unknown subscription plan '{productHandle}' for product family '{_options.Value.ProductFamilyHandle}'.");
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.GetCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _maxio.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    FirstName = shopper.FirstName,
                    LastName = shopper.LastName,
                    Email = shopper.Email,
                    Reference = shopper.UserId,
                    Organization = "eShopOnWeb"
                },
                uniquenessToken: Guid.NewGuid().ToString(),
                cancellationToken);
        }
        catch (BillingException ex) when (ex.StatusCode is 409 or 400)
        {
            var raced = await _maxio.GetCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        foreach (var subscription in subscriptions)
        {
            if (IsLive(subscription)
                && string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
            {
                return subscription;
            }
        }

        var byReference = await _maxio.FindSubscriptionByReferenceAsync(
            BuildSubscriptionReference(userId, productHandle), cancellationToken);
        if (byReference is not null && IsLive(byReference))
        {
            return byReference;
        }

        return null;
    }

    private static bool IsLive(MaxioSubscription subscription) =>
        subscription.State is not null && LiveStates.Contains(subscription.State);

    private static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"{userId}:{productHandle}";

    private void EnsureConfigured()
    {
        if (_options.Value.IsConfigured)
        {
            return;
        }

        throw new BillingException(503,
            "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle.");
    }
}

internal static class MaxioMappings
{
    public static SubscriptionPlan ToPlan(MaxioProduct product)
    {
        var cents = product.PriceInCents;
        return new SubscriptionPlan(
            product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            product.Description,
            cents / 100m,
            cents,
            product.Interval,
            product.IntervalUnit ?? "month");
    }

    public static ShopperSubscription ToShopperSubscription(MaxioSubscription subscription)
    {
        var cents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;
        var nextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt;
        return new ShopperSubscription(
            subscription.Id,
            subscription.State ?? string.Empty,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            cents / 100m,
            cents,
            nextBillingAt);
    }
}
