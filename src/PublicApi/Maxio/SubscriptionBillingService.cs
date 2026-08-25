using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Orchestrates the subscription billing flows against Maxio, which is the billing
/// system of record. Customer identity is linked by storing the eShopOnWeb user id
/// as the Maxio customer reference, which makes customer creation idempotent.
/// </summary>
public class SubscriptionBillingService
{
    // States in which an existing subscription to the same plan blocks creating a duplicate.
    // End-of-life states (canceled, expired, unpaid, trial_ended, failed_to_create, suspended)
    // allow subscribing again.
    private static readonly HashSet<string> BlockingStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "awaiting_signup",
        "past_due", "soft_failure", "on_hold", "paused"
    };

    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(IMaxioClient maxioClient, UserManager<ApplicationUser> userManager, ILogger<SubscriptionBillingService> logger)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>Resolves the authenticated shopper from the JWT principal. Null when unauthenticated/unknown.</summary>
    public async Task<ShopperIdentity?> ResolveShopperAsync(ClaimsPrincipal claimsPrincipal)
    {
        var username = claimsPrincipal.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(username);
        return user is null ? null : new ShopperIdentity(user.Id, user.Email ?? username);
    }

    public Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken = default)
        => _maxioClient.ListPlansAsync(cancellationToken);

    /// <summary>
    /// Subscribes the shopper to a plan. Idempotent: the Maxio customer is looked up (or created)
    /// by the eShopOnWeb user id, and if a live subscription to the same plan already exists it
    /// is returned instead of creating a duplicate.
    /// </summary>
    public async Task<SubscribeResult> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken = default)
    {
        var customer = await EnsureCustomerAsync(shopper, cancellationToken);

        var existing = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var duplicate = existing.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            BlockingStates.Contains(s.State));
        if (duplicate is not null)
        {
            _logger.LogInformation("User {UserId} already has subscription {SubscriptionId} to plan {PlanHandle}; returning existing.",
                shopper.UserId, duplicate.Id, productHandle);
            return new SubscribeResult(duplicate, customer.Id, AlreadyExisted: true);
        }

        var subscription = await _maxioClient.CreateSubscriptionAsync(
            productHandle,
            customer.Id,
            reference: shopper.UserId,
            uniquenessToken: Guid.NewGuid().ToString(),
            cancellationToken);
        return new SubscribeResult(subscription, customer.Id, AlreadyExisted: false);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        try
        {
            var (firstName, lastName) = DeriveNames(shopper.Email);
            return await _maxioClient.CreateCustomerAsync(new MaxioCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = shopper.Email,
                Reference = shopper.UserId
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when ((int)ex.StatusCode == 422)
        {
            // Lost a race with a concurrent request that created the customer first — re-read it.
            _logger.LogInformation("Customer create for reference {UserId} conflicted; re-reading by reference.", shopper.UserId);
            var existing = await _maxioClient.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private static (string FirstName, string LastName) DeriveNames(string email)
    {
        var localPart = email.Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart) ? ("eShop", "Customer") : (localPart, "(eShopOnWeb)");
    }
}

public record ShopperIdentity(string UserId, string Email);

public record SubscribeResult(MaxioSubscription Subscription, long CustomerId, bool AlreadyExisted);
