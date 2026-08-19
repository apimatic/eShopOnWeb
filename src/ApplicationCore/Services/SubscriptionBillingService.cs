using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    internal const string CustomerReferencePrefix = "eshop:";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "trialing",
        "assessing",
        "active",
        "soft_failure",
        "past_due",
        "paused",
        "unpaid",
        "awaiting_signup"
    };

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IOptions<MaxioSettings> settings,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        Guard.Against.NullOrWhiteSpace(_settings.ProductFamilyHandle, nameof(_settings.ProductFamilyHandle));

        return await _maxio.ListProductsForProductFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
    }

    public async Task<ShopperSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        Guard.Against.Null(shopper);
        Guard.Against.NullOrWhiteSpace(shopper.BuyerId, nameof(shopper.BuyerId));
        Guard.Against.NullOrWhiteSpace(shopper.Email, nameof(shopper.Email));
        Guard.Against.NullOrWhiteSpace(productHandle, nameof(productHandle));

        var plan = await _maxio.ReadProductByHandleAsync(productHandle, cancellationToken);
        if (plan is null)
        {
            throw new BillingException($"Subscription plan '{productHandle}' was not found.", 404);
        }

        if (!string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle) &&
            !string.Equals(plan.ProductFamilyHandle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException($"Subscription plan '{productHandle}' is not available.", 404);
        }

        var lockKey = $"{shopper.BuyerId}:{productHandle}";
        var gate = SubscribeLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for buyer {BuyerId} and plan {ProductHandle}.",
                    existing.Id, shopper.BuyerId, productHandle);
                return existing;
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(
                    productHandle,
                    customer.Id,
                    BuildSubscriptionReference(shopper.BuyerId, productHandle),
                    cancellationToken);

                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for buyer {BuyerId} on plan {ProductHandle}.",
                    created.Id, shopper.BuyerId, productHandle);
                return created;
            }
            catch (BillingException ex) when (ex.StatusCode == 422)
            {
                var raced = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
                if (raced is not null)
                {
                    return raced;
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
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        Guard.Against.Null(shopper);
        Guard.Against.NullOrWhiteSpace(shopper.BuyerId, nameof(shopper.BuyerId));

        var customer = await _maxio.ReadCustomerByReferenceAsync(BuildCustomerReference(shopper.BuyerId), cancellationToken);
        if (customer is null && !string.IsNullOrWhiteSpace(shopper.Email))
        {
            customer = await FindCustomerByEmailAsync(shopper.Email, cancellationToken);
        }

        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    internal static string BuildCustomerReference(string buyerId) => CustomerReferencePrefix + buyerId;

    internal static string BuildSubscriptionReference(string buyerId, string productHandle) =>
        $"{CustomerReferencePrefix}{buyerId}:{productHandle}";

    internal static (string FirstName, string LastName) SplitDisplayName(ShopperIdentity shopper)
    {
        var source = shopper.Email;
        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : (shopper.UserName ?? source);
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Capitalize(parts[1]) : "Customer";
        return (first, last);
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(shopper.BuyerId);
        var existing = await _maxio.ReadCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(shopper);
        var draft = new BillingCustomer
        {
            Email = shopper.Email,
            Reference = reference,
            FirstName = firstName,
            LastName = lastName
        };

        try
        {
            return await _maxio.CreateCustomerAsync(draft, cancellationToken);
        }
        catch (BillingException ex) when (ex.StatusCode == 422)
        {
            existing = await _maxio.ReadCustomerByReferenceAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var byEmail = await FindCustomerByEmailAsync(shopper.Email, cancellationToken);
            if (byEmail is null)
            {
                throw;
            }

            if (!string.Equals(byEmail.Reference, reference, StringComparison.Ordinal))
            {
                _logger.LogInformation("Reusing Maxio customer {CustomerId} for buyer {BuyerId} and updating reference.",
                    byEmail.Id, shopper.BuyerId);
                return await _maxio.UpdateCustomerAsync(byEmail.Id, draft, cancellationToken);
            }

            return byEmail;
        }
    }

    private async Task<BillingCustomer?> FindCustomerByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var matches = await _maxio.ListCustomersAsync(email, cancellationToken);
        return matches.FirstOrDefault(customer =>
            string.Equals(customer.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ShopperSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription =>
            LiveSubscriptionStates.Contains(subscription.State) &&
            string.Equals(subscription.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new BillingException(
                "Maxio billing is not configured. Set Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl).",
                503);
        }

        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new BillingException(
                "Maxio billing is not configured. Set Maxio:ProductFamilyHandle.",
                503);
        }
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        return textInfo.ToTitleCase(value.ToLowerInvariant());
    }
}
