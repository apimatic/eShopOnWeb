using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Enrolls eShopOnWeb shoppers in Maxio plans. Maxio is the system of record; customer
/// <c>reference</c> is the Identity user id so creates are idempotent across retries
/// and double-clicks.
/// </summary>
public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ShopperGates = new(StringComparer.Ordinal);

    private static readonly HashSet<string> EndedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IOptions<MaxioOptions> options,
        ILogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = RequireFamilyHandle();
        try
        {
            var products = await _maxio.ListProductsForProductFamilyAsync(familyHandle, cancellationToken);
            return products
                .Where(product => product.ArchivedAt is null)
                .Where(product => !string.IsNullOrWhiteSpace(product.Handle))
                .Select(ToPlan)
                .ToList();
        }
        catch (BillingGatewayException ex) when (ex.StatusCode == 404)
        {
            throw new BillingConfigurationException(
                $"Maxio product family '{familyHandle}' was not found on the configured site.", ex);
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        if (shopper is null || string.IsNullOrWhiteSpace(shopper.UserId))
        {
            throw new ArgumentException("A signed-in shopper is required to subscribe.", nameof(shopper));
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required to subscribe.", nameof(productHandle));
        }

        productHandle = productHandle.Trim();
        var familyHandle = RequireFamilyHandle();

        var gate = ShopperGates.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var product = await _maxio.ReadProductByHandleAsync(productHandle, cancellationToken);
            if (product is null || product.ArchivedAt is not null)
            {
                throw new PlanNotFoundException(productHandle);
            }

            var productFamilyHandle = product.ProductFamily?.Handle;
            if (!string.IsNullOrWhiteSpace(productFamilyHandle)
                && !string.Equals(productFamilyHandle, familyHandle, StringComparison.OrdinalIgnoreCase))
            {
                throw new PlanNotFoundException(productHandle);
            }

            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id!.Value, productHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Reusing Maxio subscription {SubscriptionId} for shopper {ShopperId} on plan {Plan}.",
                    existing.Id, shopper.UserId, productHandle);
                return new SubscribeResult(ToCustomerSubscription(existing), AlreadyExisted: true);
            }

            var subscriptionReference = BuildSubscriptionReference(shopper.UserId, productHandle);
            MaxioSubscription created;
            try
            {
                created = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customer.Id,
                    Reference = subscriptionReference,
                    // Spec Collection-Method: remittance enrolls without a stored payment profile.
                    PaymentCollectionMethod = "remittance"
                }, cancellationToken);
            }
            catch (BillingGatewayException ex) when (ex.StatusCode == 422)
            {
                var raced = await FindLiveSubscriptionAsync(customer.Id!.Value, productHandle, cancellationToken)
                            ?? await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (raced is null)
                {
                    throw;
                }

                _logger.LogInformation(
                    "Subscribe collided on Maxio reference {Reference}; returning subscription {SubscriptionId}.",
                    subscriptionReference, raced.Id);
                return new SubscribeResult(ToCustomerSubscription(raced), AlreadyExisted: true);
            }

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for shopper {ShopperId} on plan {Plan}.",
                created.Id, shopper.UserId, productHandle);
            return new SubscribeResult(ToCustomerSubscription(created), AlreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        if (shopper is null || string.IsNullOrWhiteSpace(shopper.UserId))
        {
            throw new ArgumentException("A signed-in shopper is required to list subscriptions.", nameof(shopper));
        }

        var customer = await _maxio.ReadCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        return subscriptions.Select(ToCustomerSubscription).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.ReadCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing?.Id is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(shopper);
        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = string.IsNullOrWhiteSpace(shopper.Email) ? $"{shopper.UserId}@eshop.local" : shopper.Email,
                Reference = shopper.UserId
            }, cancellationToken);
        }
        catch (BillingGatewayException ex) when (ex.StatusCode == 422)
        {
            var raced = await _maxio.ReadCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced?.Id is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription =>
            IsLive(subscription.State)
            && string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private string RequireFamilyHandle()
    {
        var handle = _options.Value.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingConfigurationException(
                "Maxio:ProductFamilyHandle is not configured. Set MAXIO_DEFAULT_PRODUCT_FAMILY or the Maxio:ProductFamilyHandle secret.");
        }

        if (string.IsNullOrWhiteSpace(_options.Value.ApiKey))
        {
            throw new BillingConfigurationException(
                "Maxio:ApiKey is not configured. Set MAXIO_API_KEY or the Maxio:ApiKey user-secret.");
        }

        return handle.Trim();
    }

    internal static string BuildSubscriptionReference(string userId, string productHandle)
        => $"{userId}:{productHandle}";

    internal static bool IsLive(string? state)
        => !string.IsNullOrWhiteSpace(state) && !EndedStates.Contains(state);

    internal static (string FirstName, string LastName) SplitDisplayName(ShopperIdentity shopper)
    {
        var source = shopper.Email ?? shopper.UserName ?? shopper.UserId;
        var local = source;
        var at = source.IndexOf('@');
        if (at > 0)
        {
            local = source[..at];
        }

        local = string.IsNullOrWhiteSpace(local) ? "Shopper" : local.Trim();
        if (local.Length > 40)
        {
            local = local[..40];
        }

        return (local, "eShopOnWeb");
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) =>
        new(
            product.Handle!,
            product.Name ?? product.Handle!,
            product.Description,
            checked((int)(product.PriceInCents ?? 0)),
            product.Interval ?? 1,
            product.IntervalUnit ?? "month",
            product.RequireCreditCard ?? false);

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription) =>
        new(
            subscription.Id ?? 0,
            subscription.Reference,
            subscription.State ?? "unknown",
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            checked((int)(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0)),
            subscription.NextAssessmentAt,
            subscription.CurrentPeriodEndsAt);
}
