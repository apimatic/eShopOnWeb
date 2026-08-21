using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    public const string DefaultPaymentCollectionMethod = "remittance";

    private static readonly ConcurrentDictionary<string, Lazy<SemaphoreSlim>> Gates = new(StringComparer.Ordinal);

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxio.ListProductsForProductFamilyAsync(cancellationToken);
        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(ShopperIdentity shopper, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper);
        Guard.Against.NullOrWhiteSpace(shopper.UserId);
        Guard.Against.NullOrWhiteSpace(planHandle);

        var plan = await ResolvePlanAsync(planHandle.Trim(), cancellationToken);
        var customer = await EnsureCustomerAsync(shopper, cancellationToken);

        var gate = Gates.GetOrAdd(
            $"{shopper.UserId}:{plan.Handle}",
            _ => new Lazy<SemaphoreSlim>(() => new SemaphoreSlim(1, 1))).Value;

        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await FindCurrentSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}.", existing.Id, shopper.UserId, plan.Handle);
                return new SubscribeResult(existing, Created: false);
            }

            var reference = BuildSubscriptionReference(shopper.UserId, plan.Handle);
            try
            {
                var created = await _maxio.CreateSubscriptionAsync(
                    customer.Id,
                    plan.Handle,
                    reference,
                    DefaultPaymentCollectionMethod,
                    cancellationToken);

                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}.", created.Id, shopper.UserId, plan.Handle);
                return new SubscribeResult(created, Created: true);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 422)
            {
                var recovered = await RecoverExistingSubscriptionAsync(customer.Id, plan.Handle, reference, cancellationToken);
                if (recovered is not null)
                {
                    _logger.LogInformation("Recovered existing Maxio subscription {SubscriptionId} after a 422 for user {UserId} on plan {PlanHandle}.", recovered.Id, shopper.UserId, plan.Handle);
                    return new SubscribeResult(recovered, Created: false);
                }

                var retryReference = $"{reference}:{Guid.NewGuid():N}";
                var created = await _maxio.CreateSubscriptionAsync(
                    customer.Id,
                    plan.Handle,
                    retryReference,
                    DefaultPaymentCollectionMethod,
                    cancellationToken);

                _logger.LogInformation("Created Maxio subscription {SubscriptionId} with a new reference after a 422 for user {UserId} on plan {PlanHandle}.", created.Id, shopper.UserId, plan.Handle);
                return new SubscribeResult(created, Created: true);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper);
        Guard.Against.NullOrWhiteSpace(shopper.UserId);

        var customer = await _maxio.ReadCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.NextBillingDate)
            .ToList();
    }

    public static string BuildSubscriptionReference(string userId, string planHandle)
        => $"eshop:{userId}:{planHandle}";

    internal static (string FirstName, string LastName) SplitDisplayName(ShopperIdentity shopper)
    {
        var source = !string.IsNullOrWhiteSpace(shopper.Email) ? shopper.Email : shopper.UserName;
        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : source;
        if (string.IsNullOrWhiteSpace(local))
        {
            local = "Shopper";
        }

        return (local, "eShopOnWeb");
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plan = await _maxio.ReadProductByHandleAsync(planHandle, cancellationToken);
        if (plan is null || string.IsNullOrWhiteSpace(plan.Handle))
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        if (!string.Equals(plan.ProductFamilyHandle, _maxio.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        return plan;
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.ReadCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(shopper);
        var email = !string.IsNullOrWhiteSpace(shopper.Email) ? shopper.Email : shopper.UserName;

        try
        {
            var created = await _maxio.CreateCustomerAsync(firstName, lastName, email, shopper.UserId, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            var raced = await _maxio.ReadCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<ShopperSubscription?> FindCurrentSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            s.IsCurrent &&
            string.Equals(s.ProductHandle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ShopperSubscription?> RecoverExistingSubscriptionAsync(
        int customerId,
        string planHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        var byReference = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (byReference is not null && byReference.IsCurrent)
        {
            return byReference;
        }

        return await FindCurrentSubscriptionAsync(customerId, planHandle, cancellationToken);
    }
}
