using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "assessing",
        "pending",
        "trialing",
        "paused",
        "past_due",
        "soft_failure",
        "unpaid",
        "awaiting_signup"
    };

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IOptionsMonitor<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        options.EnsureConfigured();

        var products = await _maxio.ListProductsForProductFamilyAsync(options.ProductFamilyHandle, cancellationToken);

        return products
            .Where(product => !product.IsArchived)
            .Select(ToPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        BillingCustomer customer,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        options.EnsureConfigured();

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException("A product handle is required to subscribe.", HttpStatusCode.BadRequest);
        }

        productHandle = productHandle.Trim();
        var lockKey = $"{customer.Reference}:{productHandle}";
        var gate = Locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            var plan = await RequirePlanInFamilyAsync(productHandle, options.ProductFamilyHandle, cancellationToken);
            var maxioCustomer = await EnsureCustomerAsync(customer, cancellationToken);

            var existing = await FindLiveSubscriptionAsync(maxioCustomer.Id, productHandle, lockKey, cancellationToken);
            if (existing != null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for customer {CustomerId} and plan {ProductHandle}.",
                    existing.Id, maxioCustomer.Id, productHandle);
                return ToCustomerSubscription(existing, plan);
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscriptionDto
                {
                    ProductHandle = productHandle,
                    CustomerId = maxioCustomer.Id,
                    Reference = lockKey,
                    PaymentCollectionMethod = "remittance"
                }, cancellationToken);

                return ToCustomerSubscription(created, plan);
            }
            catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                // A concurrent signup may have created the subscription (or reused the reference).
                var raced = await FindLiveSubscriptionAsync(maxioCustomer.Id, productHandle, lockKey, cancellationToken);
                if (raced != null)
                {
                    return ToCustomerSubscription(raced, plan);
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        options.EnsureConfigured();

        var maxioCustomer = await _maxio.ReadCustomerByReferenceAsync(customerReference, cancellationToken);
        if (maxioCustomer == null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(maxioCustomer.Id, cancellationToken);

        return subscriptions
            .Where(subscription =>
                string.Equals(subscription.ProductFamilyHandle, options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrEmpty(subscription.ProductFamilyHandle) && !string.IsNullOrEmpty(subscription.ProductHandle)))
            .Select(subscription => ToCustomerSubscription(subscription, plan: null))
            .ToList();
    }

    private async Task<SubscriptionPlan> RequirePlanInFamilyAsync(
        string productHandle,
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        var product = await _maxio.ReadProductByHandleAsync(productHandle, cancellationToken);
        if (product == null || product.IsArchived)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        if (!string.Equals(product.ProductFamilyHandle, productFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        return ToPlan(product);
    }

    private async Task<MaxioCustomerDto> EnsureCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken)
    {
        var existing = await _maxio.ReadCustomerByReferenceAsync(customer.Reference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCreateCustomerDto
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }, cancellationToken);
        }
        catch (BillingException ex) when (ex.StatusCode == HttpStatusCode.BadRequest && IsReferenceTaken(ex.Message))
        {
            var raced = await _maxio.ReadCustomerByReferenceAsync(customer.Reference, cancellationToken);
            if (raced != null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscriptionDto?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var byReference = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (byReference != null && IsLive(byReference) && SameProduct(byReference, productHandle))
        {
            return byReference;
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription =>
            IsLive(subscription) && SameProduct(subscription, productHandle));
    }

    private static bool SameProduct(MaxioSubscriptionDto subscription, string productHandle) =>
        string.Equals(subscription.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase);

    private static bool IsLive(MaxioSubscriptionDto subscription) =>
        LiveSubscriptionStates.Contains(subscription.State);

    private static bool IsReferenceTaken(string message) =>
        message.Contains("reference", StringComparison.OrdinalIgnoreCase)
        && (message.Contains("taken", StringComparison.OrdinalIgnoreCase)
            || message.Contains("already", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unique", StringComparison.OrdinalIgnoreCase));

    private static SubscriptionPlan ToPlan(MaxioProductDto product) =>
        new(
            product.Handle,
            product.Name,
            product.Description,
            CentsToUsd(product.PriceInCents),
            product.Interval,
            product.IntervalUnit,
            product.ProductFamilyHandle ?? string.Empty);

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscriptionDto subscription, SubscriptionPlan? plan)
    {
        var price = subscription.ProductPriceInCents != 0
            ? CentsToUsd(subscription.ProductPriceInCents)
            : plan?.Price ?? 0m;

        return new CustomerSubscription(
            subscription.Id,
            subscription.State,
            subscription.ProductHandle ?? plan?.Handle ?? string.Empty,
            subscription.ProductName ?? plan?.Name ?? subscription.ProductHandle ?? string.Empty,
            price,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            subscription.CurrentPeriodEndsAt,
            subscription.CreatedAt);
    }

    private static decimal CentsToUsd(long cents) => cents / 100m;
}
