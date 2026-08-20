using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Enrolls shoppers in Maxio plans. Maxio is the billing system of record;
/// customer.reference is the eShopOnWeb user id so lookups are idempotent.
/// </summary>
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

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();

    private readonly IMaxioBillingClient _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(IMaxioBillingClient maxio, IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => _maxio.ListPlansAsync(cancellationToken);

    public async Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        GuardShopper(shopper);

        var customer = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        GuardShopper(shopper);

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new InvalidSubscriptionRequestException("productHandle is required.");
        }

        productHandle = productHandle.Trim();

        var plans = await _maxio.ListPlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidSubscriptionRequestException(
                $"Unknown or unavailable subscription plan '{productHandle}'.");
        }

        var gateKey = $"{shopper.UserId}:{productHandle.ToLowerInvariant()}";
        var gate = EnrollmentLocks.GetOrAdd(gateKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await EnrollAsync(shopper, productHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<SubscribeResult> EnrollAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var customer = await EnsureCustomerAsync(shopper, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Shopper {UserId} already subscribed to {Plan} (Maxio subscription {SubscriptionId}).",
                shopper.UserId, productHandle, existing.Id);
            return new SubscribeResult(existing, created: false);
        }

        var uniquenessToken = CreateUniquenessToken(shopper.UserId, productHandle);
        try
        {
            var created = await _maxio.CreateSubscriptionAsync(
                customer.Id, productHandle, uniquenessToken, cancellationToken);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} on {Plan} for shopper {UserId}.",
                created.Id, productHandle, shopper.UserId);
            return new SubscribeResult(created, created: true);
        }
        catch (MaxioDuplicateSubmissionException)
        {
            _logger.LogWarning(
                "Duplicate Maxio uniqueness_token for shopper {UserId} plan {Plan}; recovering existing subscription.",
                shopper.UserId, productHandle);

            var recovered = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
            if (recovered is not null)
            {
                return new SubscribeResult(recovered, created: false);
            }

            // The prior attempt used this token but did not leave a live subscription
            // (for example a 422). Retry once with a fresh token.
            var retryToken = Guid.NewGuid().ToString();
            var created = await _maxio.CreateSubscriptionAsync(
                customer.Id, productHandle, retryToken, cancellationToken);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} on {Plan} for shopper {UserId} after uniqueness retry.",
                created.Id, productHandle, shopper.UserId);
            return new SubscribeResult(created, created: true);
        }
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(shopper);
        var uniquenessToken = CreateUniquenessToken(shopper.UserId, "customer");

        try
        {
            var created = await _maxio.CreateCustomerAsync(
                shopper.UserId,
                shopper.Email,
                firstName,
                lastName,
                uniquenessToken,
                cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for shopper {UserId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioDuplicateSubmissionException)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            // Reference uniqueness: another request created this customer first.
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<BillingSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase)
            && LiveStates.Contains(s.State));
    }

    private static void GuardShopper(ShopperIdentity shopper)
    {
        if (shopper is null || string.IsNullOrWhiteSpace(shopper.UserId))
        {
            throw new InvalidSubscriptionRequestException("An authenticated shopper is required.");
        }

        if (string.IsNullOrWhiteSpace(shopper.Email))
        {
            throw new InvalidSubscriptionRequestException("The authenticated shopper is missing an email address.");
        }
    }

    public static (string FirstName, string LastName) SplitDisplayName(ShopperIdentity shopper)
    {
        var source = !string.IsNullOrWhiteSpace(shopper.UserName) ? shopper.UserName : shopper.Email;
        var local = source.Contains('@', StringComparison.Ordinal)
            ? source.Split('@')[0]
            : source;
        local = local.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        var parts = local.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (parts[0], string.Join(' ', parts.Skip(1)));
        }

        return (parts.Length == 1 ? parts[0] : "Shopper", "eShopOnWeb");
    }

    public static string CreateUniquenessToken(string userId, string purpose)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"eshop-subscribe:{userId}:{purpose}"));
        var guidBytes = bytes.AsSpan(0, 16).ToArray();
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes).ToString();
    }
}
