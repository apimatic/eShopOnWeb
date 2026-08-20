using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the Subscribe hero flow against Maxio Advanced Billing as the system of record.
/// Customer and subscription creation are idempotent via Maxio <c>reference</c> uniqueness
/// plus a live-subscription check so a double-click cannot enroll twice.
/// </summary>
public class SubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// Terminal Advanced Billing subscription states. A shopper in one of these may subscribe again.
    /// See Maxio subscription states documentation; live states are treated as already enrolled.
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "trial_ended",
        "failed_to_create"
    };

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        MaxioOptions options,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var plans = await _maxio.ListProductsInFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return plans
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (shopper is null) throw new ArgumentNullException(nameof(shopper));

        var plans = await ListPlansAsync(cancellationToken);
        if (plans.Count == 0)
        {
            throw new BillingException((int)HttpStatusCode.BadRequest, "No subscription plans are available.");
        }

        var handle = (productHandle ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(handle))
        {
            // Default to the highest-priced plan in the family (Pro in the seeded catalog).
            handle = plans.OrderByDescending(p => p.PriceInCents).First().Handle;
        }

        if (!plans.Any(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BillingException((int)HttpStatusCode.BadRequest,
                $"Unknown subscription plan handle '{handle}'.");
        }

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var subscriptionReference = BuildSubscriptionReference(shopper, handle);

        var existing = await FindLiveSubscriptionAsync(customer.Id, handle, subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for shopper on plan {ProductHandle}.",
                existing.Id, handle);
            return new SubscribeResult(existing, created: false);
        }

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(customer.Id, handle, subscriptionReference, cancellationToken);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} on plan {ProductHandle}.",
                created.Id, handle);
            return new SubscribeResult(created, created: true);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            // Race: a concurrent request created the same referenced subscription.
            var raced = await FindLiveSubscriptionAsync(customer.Id, handle, subscriptionReference, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation("Subscribe raced; returning existing Maxio subscription {SubscriptionId} on plan {ProductHandle}.",
                    raced.Id, handle);
                return new SubscribeResult(raced, created: false);
            }

            throw new BillingException((int)HttpStatusCode.BadRequest,
                "Maxio rejected the subscription. Confirm the plan allows signup without a payment method.");
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (shopper is null) throw new ArgumentNullException(nameof(shopper));

        var customer = await _maxio.FindCustomerByReferenceAsync(BuildCustomerReference(shopper), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomerRecord> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(shopper);
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(shopper);
        var email = string.IsNullOrWhiteSpace(shopper.Email) ? shopper.UserName : shopper.Email;

        try
        {
            var created = await _maxio.CreateCustomerAsync(reference, firstName, lastName, email, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for eShopOnWeb shopper.", created.Id);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            // Duplicate reference from a concurrent create (Maxio enforces unique customer.reference).
            var raced = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw new BillingException((int)HttpStatusCode.BadRequest,
                "Unable to create a Maxio customer for this shopper.");
        }
    }

    private async Task<ShopperSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var byReference = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (byReference is not null && IsLive(byReference.State))
        {
            return byReference;
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase)
            && IsLive(s.State));
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new BillingException((int)HttpStatusCode.ServiceUnavailable,
                "Maxio Advanced Billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.");
        }
    }

    internal static string BuildCustomerReference(ShopperIdentity shopper)
    {
        var key = !string.IsNullOrWhiteSpace(shopper.Email)
            ? shopper.Email.Trim().ToLowerInvariant()
            : shopper.UserId;
        return $"eshop:{key}";
    }

    internal static string BuildSubscriptionReference(ShopperIdentity shopper, string productHandle)
        => $"{BuildCustomerReference(shopper)}:{productHandle.Trim().ToLowerInvariant()}";

    internal static bool IsLive(string? state)
        => !string.IsNullOrWhiteSpace(state) && !TerminalStates.Contains(state);

    private static (string FirstName, string LastName) DeriveName(ShopperIdentity shopper)
    {
        var source = !string.IsNullOrWhiteSpace(shopper.Email) ? shopper.Email : shopper.UserName;
        var local = source.Split('@')[0];
        var first = string.IsNullOrWhiteSpace(local) ? "Shopper" : local;
        return (first, "eShopOnWeb");
    }
}
