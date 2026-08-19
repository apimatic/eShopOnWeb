using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Enrolls eShopOnWeb users in Maxio subscriptions. Customer and subscription creation
/// are idempotent so a double-click cannot create duplicates.
/// </summary>
public class SubscriptionBillingService : ISubscriptionBillingService
{
    internal const string RemittanceCollectionMethod = "remittance";

    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "trialing",
        "assessing",
        "active",
        "soft_failure",
        "past_due",
        "paused",
        "unpaid",
        "on_hold",
        "suspended",
        "awaiting_signup"
    };

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;
    private readonly SubscriptionBillingOptions _options;
    private readonly SubscriptionIdempotencyGate _gate;

    public SubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IAppLogger<SubscriptionBillingService> logger,
        SubscriptionBillingOptions options,
        SubscriptionIdempotencyGate gate)
    {
        _maxio = maxio;
        _logger = logger;
        _options = options;
        _gate = gate;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var products = await _maxio.ListProductsForProductFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(plan => !string.IsNullOrWhiteSpace(plan.Handle))
            .OrderBy(plan => plan.Price)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        ShopperProfile shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shopper);
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required to subscribe.", nameof(productHandle));
        }

        productHandle = productHandle.Trim();

        var gate = _gate.ForUser(shopper.UserId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(shopper, productHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        ShopperProfile shopper,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shopper);
        EnsureConfigured();

        var customer = await _maxio.ReadCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(
        ShopperProfile shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        await ResolvePlanInFamilyAsync(productHandle, cancellationToken);
        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var subscriptionReference = BuildSubscriptionReference(shopper.UserId, productHandle);

        var existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for user {UserId} on plan {ProductHandle}.",
                existing.Id, shopper.UserId, productHandle);
            return new SubscribeResult(existing, created: false);
        }

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(
                customer.Id,
                productHandle,
                subscriptionReference,
                RemittanceCollectionMethod,
                cancellationToken);

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} on plan {ProductHandle}.",
                created.Id, shopper.UserId, productHandle);
            return new SubscribeResult(created, created: true);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // A concurrent signup with the same reference won the race — return the live subscription.
            var recovered = await FindLiveSubscriptionAsync(customer.Id, productHandle, subscriptionReference, cancellationToken);
            if (recovered is not null)
            {
                _logger.LogInformation("Recovered existing Maxio subscription {SubscriptionId} after a 422 for user {UserId} on plan {ProductHandle}.",
                    recovered.Id, shopper.UserId, productHandle);
                return new SubscribeResult(recovered, created: false);
            }

            throw;
        }
    }

    private async Task<SubscriptionPlan> ResolvePlanInFamilyAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plan = await _maxio.ReadProductByHandleAsync(productHandle, cancellationToken);
        if (plan is null ||
            string.IsNullOrWhiteSpace(plan.Handle) ||
            !string.Equals(plan.ProductFamilyHandle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        return plan;
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(ShopperProfile shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.ReadCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(shopper);
        var email = string.IsNullOrWhiteSpace(shopper.Email) ? shopper.UserName : shopper.Email;

        try
        {
            var created = await _maxio.CreateCustomerAsync(firstName, lastName, email, shopper.UserId, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            var recovered = await _maxio.ReadCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (recovered is not null)
            {
                _logger.LogInformation("Recovered existing Maxio customer {CustomerId} for user {UserId} after a 422.",
                    recovered.Id, shopper.UserId);
                return recovered;
            }

            throw;
        }
    }

    private async Task<ShopperSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var byReference = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (byReference is not null && IsLive(byReference))
        {
            return byReference;
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription =>
            IsLive(subscription) &&
            string.Equals(subscription.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingConfigurationException(
                "Maxio:ProductFamilyHandle is not configured. Set MAXIO_DEFAULT_PRODUCT_FAMILY or the Maxio:ProductFamilyHandle setting.");
        }
    }

    internal static bool IsLive(ShopperSubscription subscription) =>
        !string.IsNullOrWhiteSpace(subscription.State) && LiveStates.Contains(subscription.State);

    internal static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"{userId}:{productHandle}";

    internal static (string FirstName, string LastName) SplitDisplayName(ShopperProfile shopper)
    {
        var source = shopper.Email;
        if (string.IsNullOrWhiteSpace(source))
        {
            source = shopper.UserName;
        }

        var localPart = source.Contains('@', StringComparison.Ordinal)
            ? source.Split('@')[0]
            : source;

        var tokens = localPart
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            return ("Shopper", "Subscriber");
        }

        var first = Capitalize(tokens[0]);
        var last = tokens.Length > 1
            ? Capitalize(string.Join(' ', tokens.Skip(1)))
            : "Subscriber";
        return (first, last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToUpperInvariant();
        }

        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }
}
