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

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;
    private readonly string _productFamilyHandle;

    public SubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IAppLogger<SubscriptionBillingService> logger,
        string productFamilyHandle)
    {
        _maxio = maxio;
        _logger = logger;
        _productFamilyHandle = productFamilyHandle;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        return await _maxio.ListProductsForProductFamilyAsync(_productFamilyHandle, cancellationToken);
    }

    public async Task<SubscribeToPlanResult> SubscribeAsync(
        SubscribeToPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (request.Shopper is null || string.IsNullOrWhiteSpace(request.Shopper.UserId))
        {
            throw new BillingValidationException("A signed-in shopper is required to subscribe.");
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            throw new BillingValidationException("productHandle is required.");
        }

        var productHandle = request.ProductHandle.Trim();
        var lockKey = MaxioReference.ForSubscription(request.Shopper.UserId, productHandle);
        var gate = Locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            var plan = await FindPlanAsync(productHandle, cancellationToken);
            var customer = await EnsureCustomerAsync(request.Shopper, cancellationToken);
            var subscriptionReference = MaxioReference.ForSubscription(request.Shopper.UserId, plan.Handle);

            var existing = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for shopper {UserId} and plan {Plan}.",
                    existing.Id, request.Shopper.UserId, plan.Handle);
                return new SubscribeToPlanResult { Subscription = existing, AlreadySubscribed = true };
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(new CreateBillingSubscriptionRequest
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    Reference = subscriptionReference,
                    PaymentCollectionMethod = "remittance"
                }, cancellationToken);

                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for shopper {UserId} and plan {Plan}.",
                    created.Id, request.Shopper.UserId, plan.Handle);

                return new SubscribeToPlanResult { Subscription = created, AlreadySubscribed = false };
            }
            catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
            {
                var raced = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (raced is not null)
                {
                    return new SubscribeToPlanResult { Subscription = raced, AlreadySubscribed = true };
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
            throw new BillingValidationException("A signed-in shopper is required to list subscriptions.");
        }

        var customer = await _maxio.ReadCustomerByReferenceAsync(MaxioReference.ForCustomer(userId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<SubscriptionPlan> FindPlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await _maxio.ListProductsForProductFamilyAsync(_productFamilyHandle, cancellationToken);
        var plan = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));

        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        return plan;
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(ShopperProfile shopper, CancellationToken cancellationToken)
    {
        var reference = MaxioReference.ForCustomer(shopper.UserId);
        var existing = await _maxio.ReadCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ShopperName.FromProfile(shopper);
        var email = string.IsNullOrWhiteSpace(shopper.Email) ? shopper.UserName : shopper.Email;

        try
        {
            var created = await _maxio.CreateCustomerAsync(new CreateBillingCustomerRequest
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for shopper {UserId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.ReadCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_productFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Set Maxio:ProductFamilyHandle (from MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }
    }
}
