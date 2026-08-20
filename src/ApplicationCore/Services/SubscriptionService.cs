using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionService : ISubscriptionService
{
    internal const string CustomerReferencePrefix = "eshop:";
    internal const string SubscriptionReferencePrefix = "eshop-sub:";
    private const string Organization = "eShopOnWeb";

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

    private readonly IMaxioBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeGates = new(StringComparer.Ordinal);

    public SubscriptionService(
        IMaxioBillingClient maxio,
        MaxioOptions options,
        IAppLogger<SubscriptionService> logger)
    {
        _maxio = maxio;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        return await _maxio.ListProductsInFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
    }

    public async Task<CreateSubscriptionResult> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ArgumentNullException.ThrowIfNull(shopper);

        var handle = (productHandle ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new UnknownSubscriptionPlanException(productHandle ?? string.Empty);
        }

        var gate = _subscribeGates.GetOrAdd($"{shopper.UserId}:{handle}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var plans = await _maxio.ListProductsInFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
            if (!plans.Any(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnknownSubscriptionPlanException(handle);
            }

            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var subscriptionReference = BuildSubscriptionReference(shopper.UserId, handle);

            var existingByReference = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existingByReference is not null && IsLive(existingByReference.State))
            {
                _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for shopper {ShopperId} plan {Plan}.",
                    existingByReference.Id, shopper.UserId, handle);
                return new CreateSubscriptionResult(existingByReference, Created: false);
            }

            var customerSubscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var liveForPlan = customerSubscriptions.FirstOrDefault(s =>
                IsLive(s.State) && string.Equals(s.ProductHandle, handle, StringComparison.OrdinalIgnoreCase));
            if (liveForPlan is not null)
            {
                _logger.LogInformation("Returning live Maxio subscription {SubscriptionId} for shopper {ShopperId} plan {Plan}.",
                    liveForPlan.Id, shopper.UserId, handle);
                return new CreateSubscriptionResult(liveForPlan, Created: false);
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(
                    new CreateMaxioSubscriptionRequest(
                        customer.Id,
                        handle,
                        subscriptionReference,
                        Guid.NewGuid().ToString("N")),
                    cancellationToken);

                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for shopper {ShopperId} plan {Plan}.",
                    created.Id, shopper.UserId, handle);
                return new CreateSubscriptionResult(created, Created: true);
            }
            catch (MaxioApiException ex) when (ex.StatusCode is 409 or 422)
            {
                var recovered = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken)
                    ?? (await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                        .FirstOrDefault(s =>
                            IsLive(s.State)
                            && string.Equals(s.ProductHandle, handle, StringComparison.OrdinalIgnoreCase));

                if (recovered is not null)
                {
                    _logger.LogInformation("Recovered Maxio subscription {SubscriptionId} after {Status} for shopper {ShopperId} plan {Plan}.",
                        recovered.Id, ex.StatusCode, shopper.UserId, handle);
                    return new CreateSubscriptionResult(recovered, Created: false);
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
        ArgumentNullException.ThrowIfNull(shopper);

        var customer = await _maxio.FindCustomerByReferenceAsync(BuildCustomerReference(shopper.UserId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    public static string BuildCustomerReference(string userId) => CustomerReferencePrefix + userId;

    public static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"{SubscriptionReferencePrefix}{userId}:{productHandle}";

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(shopper.UserId);
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(shopper);
        try
        {
            return await _maxio.CreateCustomerAsync(
                new CreateMaxioCustomerRequest(
                    firstName,
                    lastName,
                    shopper.Email,
                    reference,
                    Organization),
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new MaxioConfigurationException(
                "Maxio Advanced Billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle.");
        }
    }

    private static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && LiveStates.Contains(state);

    public static (string FirstName, string LastName) SplitName(ShopperIdentity shopper)
    {
        var source = shopper.UserName;
        if (string.IsNullOrWhiteSpace(source) || source.Contains('@'))
        {
            source = shopper.Email.Split('@')[0];
        }

        var parts = source.Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return ("Shopper", "Customer");
        }

        if (parts.Length == 1)
        {
            return (parts[0], "Customer");
        }

        return (parts[0], string.Join(" ", parts.Skip(1)));
    }
}
