using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "trialing",
        "pending",
        "assessing",
        "past_due",
        "unpaid",
        "paused",
        "soft_failure",
        "awaiting_signup",
        "on_hold",
        "suspended"
    };

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly SubscriptionConcurrencyGate _gate;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IOptions<MaxioOptions> options,
        SubscriptionConcurrencyGate gate,
        ILogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _gate = gate;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureFamilyConfigured();
        var products = await _maxio.ListProductsForProductFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public Task<CustomerSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException(400, "A productHandle is required.");
        }

        EnsureFamilyConfigured();

        var key = $"{shopper.UserId}:{productHandle}";
        return _gate.RunAsync(key, () => SubscribeCoreAsync(shopper, productHandle.Trim(), cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        var customer = await _maxio.ReadCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        return subscriptions.Select(s => MapSubscription(s, alreadyExisted: true)).ToList();
    }

    private async Task<CustomerSubscription> SubscribeCoreAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var plan = await FindPlanAsync(productHandle, cancellationToken);
        if (plan is null)
        {
            throw new BillingException(404, $"Subscription plan '{productHandle}' was not found in the configured product family.");
        }

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var existing = await FindLiveSubscriptionAsync(customer.Id!.Value, productHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Returning existing Maxio subscription {SubscriptionId} for shopper {ShopperId} plan {Plan}",
                existing.Id, shopper.UserId, productHandle);
            return MapSubscription(existing, alreadyExisted: true);
        }

        var reference = BuildSubscriptionReference(shopper.UserId, productHandle);
        var byReference = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (byReference is not null && IsLive(byReference.State))
        {
            return MapSubscription(byReference, alreadyExisted: true);
        }

        if (byReference is not null)
        {
            // Reference is taken by an end-of-life subscription; use a unique suffix so create can proceed.
            reference = $"{reference}:{Guid.NewGuid():N}";
        }

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customer.Id,
                Reference = reference,
                // Spec Collection-Method: remittance is valid on Relationship Invoicing and does not require a card.
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);

            return MapSubscription(created, alreadyExisted: false);
        }
        catch (BillingException ex) when (ex.StatusCode == 422)
        {
            var raced = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken)
                        ?? await FindLiveSubscriptionAsync(customer.Id.Value, productHandle, cancellationToken);
            if (raced is not null)
            {
                return MapSubscription(raced, alreadyExisted: true);
            }

            throw;
        }
    }

    private async Task<Customer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.ReadCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing?.Id is not null)
        {
            return existing;
        }

        try
        {
            return await _maxio.CreateCustomerAsync(new CreateCustomer
            {
                FirstName = shopper.FirstName,
                LastName = shopper.LastName,
                Email = shopper.Email,
                Reference = shopper.UserId
            }, cancellationToken);
        }
        catch (BillingException ex) when (ex.StatusCode == 422)
        {
            var raced = await _maxio.ReadCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced?.Id is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            IsLive(s.State)
            && string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<SubscriptionPlan?> FindPlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureFamilyConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingException(503, "Maxio:ProductFamilyHandle is not configured.");
        }
    }

    private SubscriptionPlan MapPlan(Product product)
    {
        return new SubscriptionPlan(
            Handle: product.Handle!,
            Name: product.Name ?? product.Handle!,
            Description: product.Description,
            PriceInCents: product.PriceInCents ?? 0,
            Interval: product.Interval ?? 1,
            IntervalUnit: product.IntervalUnit ?? "month",
            ProductFamilyHandle: product.ProductFamily?.Handle ?? _options.ProductFamilyHandle);
    }

    private static CustomerSubscription MapSubscription(Subscription subscription, bool alreadyExisted)
    {
        var nextBilling = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt;
        return new CustomerSubscription(
            Id: subscription.Id ?? 0,
            State: subscription.State ?? "unknown",
            PlanHandle: subscription.Product?.Handle ?? string.Empty,
            PlanName: subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            PriceInCents: subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
            NextBillingAt: nextBilling,
            CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            ActivatedAt: subscription.ActivatedAt,
            AlreadyExisted: alreadyExisted);
    }

    internal static string BuildSubscriptionReference(string userId, string productHandle)
        => $"eshop:{userId}:{productHandle}";

    private static bool IsLive(string? state)
        => !string.IsNullOrWhiteSpace(state) && LiveStates.Contains(state);
}
