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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserGates = new(StringComparer.Ordinal);

    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "trialing",
        "assessing",
        "active",
        "soft_failure",
        "past_due",
        "paused",
        "unpaid"
    };

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = _options.Value.ProductFamilyHandle;
        Guard.Against.NullOrWhiteSpace(familyHandle, nameof(MaxioOptions.ProductFamilyHandle));

        var products = await _maxio.ListProductsForProductFamilyAsync(familyHandle, cancellationToken);
        return products
            .Where(product => string.Equals(product.ProductFamilyHandle, familyHandle, StringComparison.OrdinalIgnoreCase)
                              || string.IsNullOrWhiteSpace(product.ProductFamilyHandle))
            .Select(MapPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper);

        var customer = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    public async Task<ShopperSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper);
        Guard.Against.NullOrWhiteSpace(productHandle, nameof(productHandle));

        var plan = await FindPlanAsync(productHandle, cancellationToken);
        var gate = UserGates.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for shopper {ShopperId} on plan {PlanHandle}",
                    existing.Id, shopper.UserId, plan.Handle);
                return MapSubscription(existing);
            }

            var reference = BuildSubscriptionReference(shopper.UserId, plan.Handle);
            var byReference = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (byReference is not null && IsLive(byReference.State))
            {
                return MapSubscription(byReference);
            }

            try
            {
                // Spec: createSubscription with product_handle + customer_id.
                // payment_collection_method=remittance is the documented no-card signup path.
                var created = await _maxio.CreateSubscriptionAsync(
                    customer.Id,
                    plan.Handle,
                    byReference is null ? reference : null,
                    paymentCollectionMethod: "remittance",
                    cancellationToken);

                return MapSubscription(created);
            }
            catch (BillingProviderException ex) when (ex.StatusCode == 422)
            {
                var raced = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken)
                            ?? await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
                if (raced is not null)
                {
                    return MapSubscription(raced);
                }

                throw new BillingValidationException(ex.Message);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<MaxioCustomerSnapshot> EnsureCustomerAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            // Spec: createCustomer. reference is unique per site ("one customer for a given reference value").
            return await _maxio.CreateCustomerAsync(
                shopper.FirstName,
                shopper.LastName,
                shopper.Email,
                shopper.UserId,
                cancellationToken);
        }
        catch (BillingProviderException ex) when (ex.StatusCode == 422)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw new BillingValidationException(ex.Message);
        }
    }

    private async Task<MaxioSubscriptionSnapshot?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription =>
            IsLive(subscription.State)
            && string.Equals(subscription.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<SubscriptionPlan> FindPlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(item =>
            string.Equals(item.Handle, productHandle, StringComparison.OrdinalIgnoreCase));

        if (plan is null)
        {
            throw new BillingValidationException(
                $"Unknown subscription plan '{productHandle}' for product family '{_options.Value.ProductFamilyHandle}'.");
        }

        return plan;
    }

    internal static string BuildSubscriptionReference(string userId, string productHandle)
    {
        // Deterministic per shopper+plan so a double-click cannot create two live subscriptions.
        var sanitizedUser = SanitizeReferenceToken(userId);
        var sanitizedHandle = SanitizeReferenceToken(productHandle);
        return $"eshop-{sanitizedUser}-{sanitizedHandle}";
    }

    private static string SanitizeReferenceToken(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        return new string(chars).Trim('-');
    }

    private static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && LiveStates.Contains(state);

    private static SubscriptionPlan MapPlan(MaxioProductSnapshot product) => new()
    {
        Id = product.Id,
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static ShopperSubscription MapSubscription(MaxioSubscriptionSnapshot subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle ?? string.Empty,
        ProductName = subscription.ProductName ?? subscription.ProductHandle ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        Price = subscription.ProductPriceInCents / 100m,
        State = subscription.State,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        Reference = subscription.Reference
    };
}
