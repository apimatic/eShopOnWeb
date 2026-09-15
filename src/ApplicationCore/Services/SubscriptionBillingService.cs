using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    public const string CustomerReferencePrefix = "eshop:";

    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        MaxioSettings settings,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _settings = settings;
        _logger = logger;
    }

    public static string CustomerReferenceFor(string shopperUserId) =>
        $"{CustomerReferencePrefix}{shopperUserId}";

    public static string SubscriptionReferenceFor(string shopperUserId, string productHandle) =>
        $"{CustomerReferencePrefix}{shopperUserId}:{productHandle}";

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var products = await _maxio.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => !p.Archived && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(ToPlan)
            .ToList();
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        string shopperUserId,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(shopperUserId, nameof(shopperUserId));
        EnsureConfigured();

        var customer = await _maxio.GetCustomerByReferenceAsync(CustomerReferenceFor(shopperUserId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToShopperSubscription).ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken)
    {
        Guard.Against.Null(request, nameof(request));
        Guard.Against.NullOrEmpty(request.ShopperUserId, nameof(request.ShopperUserId));
        Guard.Against.NullOrEmpty(request.Email, nameof(request.Email));
        Guard.Against.NullOrEmpty(request.ProductHandle, nameof(request.ProductHandle));
        EnsureConfigured();

        var plan = await FindPlanAsync(request.ProductHandle, cancellationToken)
            ?? throw new SubscriptionPlanNotFoundException(request.ProductHandle);

        var gate = SubscribeLocks.GetOrAdd(
            $"{request.ShopperUserId}:{plan.Handle}",
            _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(request, cancellationToken);
            var existing = await FindCurrentEnrollmentAsync(customer.Id, plan.Handle, request.ShopperUserId, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for shopper {ShopperUserId} on plan {ProductHandle}.",
                    existing.Id, request.ShopperUserId, plan.Handle);
                return new SubscribeResult { Subscription = ToShopperSubscription(existing), Created = false };
            }

            var uniquenessToken = Guid.NewGuid().ToString("D");
            var created = await CreateSubscriptionIdempotentAsync(customer.Id, plan.Handle, request.ShopperUserId, uniquenessToken, cancellationToken);
            return new SubscribeResult { Subscription = ToShopperSubscription(created), Created = true };
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken)
    {
        var reference = CustomerReferenceFor(request.ShopperUserId);
        var existing = await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _maxio.CreateCustomerAsync(new CreateMaxioCustomerRequest
            {
                FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? "Shopper" : request.FirstName,
                LastName = string.IsNullOrWhiteSpace(request.LastName) ? "eShopOnWeb" : request.LastName,
                Email = request.Email,
                Reference = reference
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for shopper {ShopperUserId}.", created.Id, request.ShopperUserId);
            return created;
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == 422)
        {
            var raced = await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindCurrentEnrollmentAsync(
        long customerId,
        string productHandle,
        string shopperUserId,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        var current = subscriptions.FirstOrDefault(s =>
            IsCurrentEnrollment(s.State) &&
            string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (current is not null)
        {
            return current;
        }

        var byReference = await _maxio.FindSubscriptionByReferenceAsync(
            SubscriptionReferenceFor(shopperUserId, productHandle), cancellationToken);
        if (byReference is not null && IsCurrentEnrollment(byReference.State) &&
            string.Equals(byReference.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase))
        {
            return byReference;
        }

        return null;
    }

    private async Task<MaxioSubscription> CreateSubscriptionIdempotentAsync(
        long customerId,
        string productHandle,
        string shopperUserId,
        string uniquenessToken,
        CancellationToken cancellationToken)
    {
        var reference = SubscriptionReferenceFor(shopperUserId, productHandle);
        try
        {
            var created = await _maxio.CreateSubscriptionAsync(new CreateMaxioSubscriptionRequest
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                Reference = reference,
                UniquenessToken = uniquenessToken,
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for shopper {ShopperUserId} on plan {ProductHandle}.",
                created.Id, shopperUserId, productHandle);
            return created;
        }
        catch (DuplicateException)
        {
            var recovered = await FindCurrentEnrollmentAsync(customerId, productHandle, shopperUserId, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw;
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == 422)
        {
            var recovered = await FindCurrentEnrollmentAsync(customerId, productHandle, shopperUserId, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw;
        }
    }

    private async Task<SubscriptionPlan?> FindPlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new MaxioConfigurationException(
                "Maxio Advanced Billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.");
        }
    }

    private static bool IsCurrentEnrollment(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !EndOfLifeStates.Contains(state);

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        ProductFamilyHandle = product.ProductFamilyHandle
    };

    private static ShopperSubscription ToShopperSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        PriceInCents = subscription.ProductPriceInCents,
        Price = subscription.ProductPriceInCents / 100m,
        NextBillingAt = subscription.CurrentPeriodEndsAt
    };
}
