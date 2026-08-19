using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private readonly IMaxioClient _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(IMaxioClient maxio, IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        return _maxio.ListProductsForConfiguredFamilyAsync(cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(string userId, string email, string? displayName, string productHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        var plans = await _maxio.ListProductsForConfiguredFamilyAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var subscriptionReference = BuildSubscriptionReference(userId, productHandle);
        var existingByReference = await _maxio.GetSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existingByReference != null)
        {
            _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for user {UserId} plan {ProductHandle}.", existingByReference.Id, userId, productHandle);
            return new SubscribeResult(existingByReference, created: false);
        }

        var customer = await GetOrCreateCustomerAsync(userId, email, displayName, cancellationToken);

        var existingForCustomer = (await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
            .FirstOrDefault(s =>
                string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase)
                && SubscriptionStates.RepresentsExistingEnrollment(s.State));
        if (existingForCustomer != null)
        {
            _logger.LogInformation("User {UserId} already enrolled in {ProductHandle} as Maxio subscription {SubscriptionId}.", userId, productHandle, existingForCustomer.Id);
            return new SubscribeResult(existingForCustomer, created: false);
        }

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(customer.Id, productHandle, subscriptionReference, cancellationToken);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} plan {ProductHandle}.", created.Id, userId, productHandle);
            return new SubscribeResult(created, created: true);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.GetSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (raced != null)
            {
                return new SubscribeResult(raced, created: false);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));

        var customer = await _maxio.GetCustomerByReferenceAsync(userId, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    public static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"{userId}:{productHandle}";

    private async Task<BillingCustomer> GetOrCreateCustomerAsync(string userId, string email, string? displayName, CancellationToken cancellationToken)
    {
        var existing = await _maxio.GetCustomerByReferenceAsync(userId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(displayName, email);

        try
        {
            var created = await _maxio.CreateCustomerAsync(userId, email, firstName, lastName, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Id, userId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.GetCustomerByReferenceAsync(userId, cancellationToken);
            if (raced != null)
            {
                return raced;
            }

            throw;
        }
    }

    public static (string FirstName, string LastName) SplitDisplayName(string? displayName, string email)
    {
        var source = !string.IsNullOrWhiteSpace(displayName) ? displayName! : email;
        var local = source.Contains('@') ? source.Split('@')[0] : source;
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2)
        {
            return (Capitalize(parts[0]), Capitalize(parts[1]));
        }

        var last = string.IsNullOrWhiteSpace(local) ? "User" : local;
        return ("Shopper", last);
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
