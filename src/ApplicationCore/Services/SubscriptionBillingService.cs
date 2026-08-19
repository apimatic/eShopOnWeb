using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
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
        "awaiting_signup"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly IAdvancedBillingGateway _gateway;
    private readonly IBillingCatalogSettings _catalogSettings;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IAdvancedBillingGateway gateway,
        IBillingCatalogSettings catalogSettings,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _gateway = gateway;
        _catalogSettings = catalogSettings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureCatalogConfigured();

        try
        {
            return await _gateway.ListProductsForFamilyAsync(_catalogSettings.ProductFamilyHandle, cancellationToken);
        }
        catch (AdvancedBillingException ex)
        {
            throw WrapGatewayError("Unable to list subscription plans.", ex);
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shopper);
        if (string.IsNullOrWhiteSpace(shopper.UserId))
        {
            throw new SubscriptionBillingException("A signed-in shopper is required.", 401);
        }

        try
        {
            var customer = await _gateway.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (customer is null)
            {
                return Array.Empty<ShopperSubscription>();
            }

            return await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        }
        catch (AdvancedBillingException ex)
        {
            throw WrapGatewayError("Unable to list subscriptions.", ex);
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shopper);
        if (string.IsNullOrWhiteSpace(shopper.UserId))
        {
            throw new SubscriptionBillingException("A signed-in shopper is required.", 401);
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionBillingException("ProductHandle is required.");
        }

        EnsureCatalogConfigured();

        var gate = SubscribeLocks.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(shopper, productHandle.Trim(), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        SubscriptionPlan plan;
        try
        {
            var plans = await _gateway.ListProductsForFamilyAsync(_catalogSettings.ProductFamilyHandle, cancellationToken);
            plan = plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
                ?? throw new SubscriptionBillingException($"Unknown subscription plan '{productHandle}'.");
        }
        catch (AdvancedBillingException ex)
        {
            throw WrapGatewayError("Unable to load subscription plans.", ex);
        }

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var subscriptionReference = $"{shopper.UserId}:{plan.Handle}";

        try
        {
            var existing = await FindExistingLiveSubscriptionAsync(customer.Id, plan.Handle, subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}.",
                    existing.Id, shopper.UserId, plan.Handle);
                return new SubscribeResult { Subscription = existing, Created = false };
            }

            var createReference = await ResolveCreateReferenceAsync(subscriptionReference, cancellationToken);
            var created = await _gateway.CreateSubscriptionAsync(new CreateBillingSubscription
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                Reference = createReference,
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}.",
                created.Id, shopper.UserId, plan.Handle);

            return new SubscribeResult { Subscription = created, Created = true };
        }
        catch (AdvancedBillingException ex) when (ex.StatusCode == 422)
        {
            var raced = await FindExistingLiveSubscriptionAsync(customer.Id, plan.Handle, subscriptionReference, cancellationToken);
            if (raced is not null)
            {
                return new SubscribeResult { Subscription = raced, Created = false };
            }

            throw WrapGatewayError("Unable to create subscription.", ex);
        }
        catch (AdvancedBillingException ex)
        {
            throw WrapGatewayError("Unable to create subscription.", ex);
        }
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _gateway.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var (firstName, lastName) = ShopperNameFormatter.FromIdentity(shopper);
            try
            {
                return await _gateway.CreateCustomerAsync(new CreateBillingCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = shopper.Email,
                    Reference = shopper.UserId
                }, cancellationToken);
            }
            catch (AdvancedBillingException ex) when (ex.StatusCode == 422)
            {
                var raced = await _gateway.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
                if (raced is not null)
                {
                    return raced;
                }

                throw WrapGatewayError("Unable to create billing customer.", ex);
            }
        }
        catch (AdvancedBillingException ex)
        {
            throw WrapGatewayError("Unable to resolve billing customer.", ex);
        }
    }

    private async Task<ShopperSubscription?> FindExistingLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var byReference = await _gateway.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (byReference is not null && IsLive(byReference) && MatchesPlan(byReference, productHandle))
        {
            return byReference;
        }

        var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s => IsLive(s) && MatchesPlan(s, productHandle));
    }

    private async Task<string> ResolveCreateReferenceAsync(string subscriptionReference, CancellationToken cancellationToken)
    {
        var existing = await _gateway.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is null)
        {
            return subscriptionReference;
        }

        return $"{subscriptionReference}:{Guid.NewGuid():N}";
    }

    private void EnsureCatalogConfigured()
    {
        if (string.IsNullOrWhiteSpace(_catalogSettings.ProductFamilyHandle))
        {
            throw new SubscriptionBillingException("Maxio product family is not configured.", 500);
        }
    }

    private static bool IsLive(ShopperSubscription subscription) =>
        !string.IsNullOrWhiteSpace(subscription.State) && LiveStates.Contains(subscription.State);

    private static bool MatchesPlan(ShopperSubscription subscription, string productHandle) =>
        string.Equals(subscription.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase);

    private SubscriptionBillingException WrapGatewayError(string message, AdvancedBillingException ex)
    {
        _logger.LogWarning("Advanced Billing request failed ({StatusCode}): {Detail}", ex.StatusCode, ex.Message);

        var statusCode = ex.StatusCode >= 400 && ex.StatusCode < 500 && ex.StatusCode != 401 && ex.StatusCode != 403
            ? ex.StatusCode
            : 502;

        var detail = string.IsNullOrWhiteSpace(ex.Message) ? message : $"{message} {ex.Message}";
        return new SubscriptionBillingException(detail, statusCode, ex);
    }
}
