using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionService : ISubscriptionService
{
    internal const string CustomerReferencePrefix = "eshop:";
    internal static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "assessing",
        "pending",
        "trialing",
        "paused",
        "past_due",
        "soft_failure",
        "unpaid"
    };

    private readonly ISubscriptionBillingGateway _billingGateway;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly IMaxioSettings _maxioSettings;

    public SubscriptionService(
        ISubscriptionBillingGateway billingGateway,
        IAppLogger<SubscriptionService> logger,
        IMaxioSettings maxioSettings)
    {
        _billingGateway = billingGateway;
        _logger = logger;
        _maxioSettings = maxioSettings;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = RequireProductFamilyHandle();
        return _billingGateway.ListPlansAsync(familyHandle, cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper, nameof(shopper));
        Guard.Against.NullOrWhiteSpace(shopper.UserId, nameof(shopper.UserId));
        Guard.Against.NullOrWhiteSpace(shopper.Email, nameof(shopper.Email));

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new InvalidSubscriptionRequestException("A productHandle is required to subscribe.");
        }

        productHandle = productHandle.Trim();

        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(plan => string.Equals(plan.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidSubscriptionRequestException(
                $"Unknown subscription plan '{productHandle}'.");
        }

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var existing = await _billingGateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var liveMatch = existing.FirstOrDefault(subscription =>
            IsLive(subscription) &&
            string.Equals(subscription.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase));

        if (liveMatch is not null)
        {
            _logger.LogInformation(
                "Returning existing Maxio subscription {SubscriptionId} for shopper {UserId} on plan {ProductHandle}.",
                liveMatch.Id,
                shopper.UserId,
                productHandle);
            return new SubscribeResult(liveMatch, Created: false);
        }

        var subscriptionReference = BuildSubscriptionReference(shopper.UserId, productHandle, existing.Count);
        var byReference = await _billingGateway.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (byReference is not null && IsLive(byReference))
        {
            return new SubscribeResult(byReference, Created: false);
        }

        if (byReference is not null)
        {
            subscriptionReference = BuildSubscriptionReference(shopper.UserId, productHandle, existing.Count + 1);
        }

        var uniquenessToken = CreateStableUniquenessToken($"remittance:{subscriptionReference}");

        try
        {
            var created = await _billingGateway.CreateSubscriptionAsync(
                customer.Id,
                productHandle,
                uniquenessToken,
                subscriptionReference,
                cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for shopper {UserId} on plan {ProductHandle}.",
                created.Id,
                shopper.UserId,
                productHandle);

            return new SubscribeResult(created, Created: true);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 409)
        {
            _logger.LogWarning(
                "Duplicate Maxio subscribe prevented for shopper {UserId} on plan {ProductHandle}; returning existing enrollment.",
                shopper.UserId,
                productHandle);

            var afterConflict = await _billingGateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var recovered = afterConflict.FirstOrDefault(subscription =>
                IsLive(subscription) &&
                string.Equals(subscription.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase))
                ?? await _billingGateway.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);

            if (recovered is null)
            {
                throw;
            }

            return new SubscribeResult(recovered, Created: false);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper, nameof(shopper));
        Guard.Against.NullOrWhiteSpace(shopper.UserId, nameof(shopper.UserId));

        var customer = await _billingGateway.FindCustomerByReferenceAsync(
            BuildCustomerReference(shopper.UserId),
            cancellationToken);

        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _billingGateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    internal static string BuildCustomerReference(string userId) => $"{CustomerReferencePrefix}{userId}";

    internal static bool IsLive(CustomerSubscription subscription) =>
        LiveSubscriptionStates.Contains(subscription.State);

    internal static string BuildSubscriptionReference(string userId, string productHandle, int existingCount) =>
        $"{CustomerReferencePrefix}{userId}:{productHandle}:{existingCount}";

    internal static string CreateStableUniquenessToken(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes).ToString();
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(shopper.UserId);
        var existing = await _billingGateway.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _billingGateway.CreateCustomerAsync(shopper, reference, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for shopper {UserId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode is 409 or 422)
        {
            var raced = await _billingGateway.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private string RequireProductFamilyHandle()
    {
        var handle = _maxioSettings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new MaxioConfigurationException(
                "Maxio:ProductFamilyHandle is not configured. Set it from MAXIO_DEFAULT_PRODUCT_FAMILY.");
        }

        return handle.Trim();
    }
}
