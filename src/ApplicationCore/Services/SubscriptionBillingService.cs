using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new(StringComparer.Ordinal);

    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "trialing",
        "assessing",
        "pending",
        "past_due",
        "soft_failure",
        "unpaid",
        "paused",
        "awaiting_signup"
    };

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;
    private readonly MaxioSettings _settings;

    public SubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IAppLogger<SubscriptionBillingService> logger,
        MaxioSettings settings)
    {
        _maxio = maxio;
        _logger = logger;
        _settings = settings;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        return await _maxio.ListProductsForFamilyAsync(_settings.ProductFamilyHandle!, cancellationToken);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var family = _settings.ProductFamilyHandle;
        return subscriptions
            .Where(s => string.IsNullOrEmpty(s.ProductFamilyHandle)
                        || string.Equals(s.ProductFamilyHandle, family, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(Shopper shopper, string productHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioValidationException(new[] { "A productHandle is required." });
        }

        productHandle = productHandle.Trim();

        var plan = await _maxio.GetProductByHandleAsync(productHandle, cancellationToken)
                   ?? throw new SubscriptionPlanNotFoundException(productHandle);

        if (!string.Equals(plan.ProductFamilyHandle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var gateKey = $"{shopper.UserId}:{productHandle}";
        var gate = SubscribeGates.GetOrAdd(gateKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);

            var existing = await FindLiveSubscriptionAsync(customer.Id, shopper.UserId, productHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for user {UserId} on plan {ProductHandle}.", existing.Id, shopper.UserId, productHandle);
                return new SubscribeResult(existing, Created: false);
            }

            var reference = BuildSubscriptionReference(shopper.UserId, productHandle);
            var uniquenessToken = BuildUniquenessToken(shopper.UserId, productHandle);

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(
                    new CreateBillingSubscription(productHandle, customer.Id, reference, uniquenessToken),
                    cancellationToken);

                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} on plan {ProductHandle}.", created.Id, shopper.UserId, productHandle);
                return new SubscribeResult(created, Created: true);
            }
            catch (MaxioDuplicateException)
            {
                var recovered = await FindLiveSubscriptionAsync(customer.Id, shopper.UserId, productHandle, cancellationToken);
                if (recovered is not null)
                {
                    return new SubscribeResult(recovered, Created: false);
                }

                // Same uniqueness token within 60 minutes after a canceled subscription: retry with a fresh token.
                var retry = await _maxio.CreateSubscriptionAsync(
                    new CreateBillingSubscription(
                        productHandle,
                        customer.Id,
                        $"{reference}:{Guid.NewGuid():N}",
                        Guid.NewGuid().ToString("N")),
                    cancellationToken);

                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} on plan {ProductHandle} after uniqueness-token retry.", retry.Id, shopper.UserId, productHandle);
                return new SubscribeResult(retry, Created: true);
            }
            catch (MaxioValidationException ex) when (LooksLikeDuplicate(ex))
            {
                var recovered = await FindLiveSubscriptionAsync(customer.Id, shopper.UserId, productHandle, cancellationToken);
                if (recovered is not null)
                {
                    return new SubscribeResult(recovered, Created: false);
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(Shopper shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(shopper);
        try
        {
            var created = await _maxio.CreateCustomerAsync(
                new CreateBillingCustomer(shopper.UserId, shopper.Email, firstName, lastName),
                cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for eShopOnWeb user {UserId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioValidationException)
        {
            existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private async Task<BillingSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var byReference = await _maxio.FindSubscriptionByReferenceAsync(
            BuildSubscriptionReference(userId, productHandle), cancellationToken);
        if (byReference is not null && IsLiveForPlan(byReference, productHandle))
        {
            return byReference;
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s => IsLiveForPlan(s, productHandle));
    }

    private static bool IsLiveForPlan(BillingSubscription subscription, string productHandle)
    {
        return string.Equals(subscription.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase)
               && LiveStates.Contains(subscription.State);
    }

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new MaxioConfigurationException(
                "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.");
        }
    }

    internal static string BuildSubscriptionReference(string userId, string productHandle)
        => $"eshop:{userId}:{productHandle}";

    internal static string BuildUniquenessToken(string userId, string productHandle)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"eshop-sub:{userId}:{productHandle}"));
        return Convert.ToHexString(bytes);
    }

    internal static (string FirstName, string LastName) SplitName(Shopper shopper)
    {
        var source = !string.IsNullOrWhiteSpace(shopper.UserName) ? shopper.UserName : shopper.Email;
        var local = source.Contains('@', StringComparison.Ordinal)
            ? source[..source.IndexOf('@')]
            : source;

        var parts = local.Split(new[] { '.', '-', '_', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? ToTitle(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? ToTitle(parts[1]) : "Customer";
        return (first, last);
    }

    private static string ToTitle(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }

    private static bool LooksLikeDuplicate(MaxioValidationException exception)
    {
        return exception.Errors.Any(e =>
            e.Contains("reference", StringComparison.OrdinalIgnoreCase)
            && e.Contains("taken", StringComparison.OrdinalIgnoreCase));
    }
}
