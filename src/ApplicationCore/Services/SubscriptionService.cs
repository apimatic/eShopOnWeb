using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Application service that drives the Maxio subscription flows: plan browsing,
/// subscribing an eShopOnWeb user to a plan, and listing the user's subscriptions.
/// Maxio is the billing system of record: every call resolves the user's identity to a
/// deterministic Maxio customer/subscription reference, so subscribe is idempotent -
/// concurrent or repeated requests (double-clicks) converge on exactly one Maxio
/// customer and one subscription per user+plan.
/// </summary>
public class SubscriptionService
{
    private const string SUBSCRIPTION_REFERENCE_PREFIX = "eshoponweb";

    /// <summary>Spec Collection-Method value for invoice/offline payment collection.</summary>
    private const string REMITTANCE_COLLECTION = "remittance";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly IMaxioBillingGateway _gateway;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IMaxioBillingGateway gateway, IAppLogger<SubscriptionService> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    /// <summary>Active (non-archived) plans offered for subscription, ordered by price.</summary>
    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _gateway.ListPlansAsync(cancellationToken);
        return plans
            .Where(p => !p.IsArchived)
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Ensures a Maxio customer exists for the user, then enrolls them in the given plan.
    /// Returns <see cref="SubscriptionSignupResult.Created"/> = false when the enrollment
    /// already existed (idempotent replay).
    /// </summary>
    public async Task<SubscriptionSignupResult> SubscribeAsync(
        string userReference,
        string planHandle,
        string? firstName = null,
        string? lastName = null,
        CancellationToken cancellationToken = default)
    {
        GuardAgainstBlank(userReference, nameof(userReference));
        GuardAgainstBlank(planHandle, nameof(planHandle));

        var plans = await GetAvailablePlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new PlanNotFoundException(planHandle);

        var customerReference = userReference.Trim();
        var subscriptionReference = BuildSubscriptionReference(customerReference, plan.Handle);

        // Serialize subscribe attempts for the same user+plan (double-click protection)
        // even when they race each other through the network layer.
        var gate = SubscribeLocks.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(customerReference, firstName, lastName, cancellationToken);

            var existing = await _gateway.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Subscribe replay: user '{User}' already enrolled in plan '{Plan}' (subscription {SubscriptionId}).",
                    customerReference, plan.Handle, existing.Id);
                return new SubscriptionSignupResult(existing, Created: false);
            }

            try
            {
                var created = await _gateway.CreateSubscriptionAsync(
                    new NewBillingSubscription
                    {
                        PlanHandle = plan.Handle,
                        CustomerReference = customerReference,
                        Reference = subscriptionReference,
                        // Plans that do not require a payment method are enrolled on remittance
                        // collection: Maxio issues invoices instead of attempting card capture,
                        // which is the spec-supported way to subscribe without a payment profile.
                        PaymentCollectionMethod = plan.RequiresPaymentMethod ? null : REMITTANCE_COLLECTION
                    },
                    cancellationToken);

                _logger.LogInformation(
                    "Subscribe created: user '{User}' enrolled in plan '{Plan}' (subscription {SubscriptionId}, state {State}).",
                    customerReference, plan.Handle, created.Id, created.State);

                return new SubscriptionSignupResult(created, Created: true);
            }
            catch (MaxioReferenceConflictException)
            {
                // Lost a race against a duplicate request elsewhere: the winner already
                // created the subscription. Re-read and replay it.
                var replay = await _gateway.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken)
                    ?? throw new MaxioApiException(409,
                        $"Maxio reported subscription reference '{subscriptionReference}' as already taken, but the subscription could not be read back.");

                _logger.LogWarning(
                    "Subscribe conflict resolved as replay for user '{User}', plan '{Plan}' (subscription {SubscriptionId}).",
                    customerReference, plan.Handle, replay.Id);

                return new SubscriptionSignupResult(replay, Created: false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Lists all subscriptions recorded in Maxio for the user. Returns an empty list when
    /// the user has never been provisioned as a Maxio customer.
    /// </summary>
    public async Task<IReadOnlyList<BillingSubscription>> GetCustomerSubscriptionsAsync(
        string userReference,
        CancellationToken cancellationToken = default)
    {
        GuardAgainstBlank(userReference, nameof(userReference));

        var customer = await _gateway.FindCustomerByReferenceAsync(userReference.Trim(), cancellationToken);
        if (customer is null)
            return Array.Empty<BillingSubscription>();

        var subscriptions = await _gateway.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(
        string customerReference,
        string? firstName,
        string? lastName,
        CancellationToken cancellationToken)
    {
        var existing = await _gateway.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
            return existing;

        var (derivedFirst, derivedLast) = DeriveCustomerName(customerReference);
        try
        {
            var created = await _gateway.CreateCustomerAsync(
                new NewBillingCustomer
                {
                    Reference = customerReference,
                    Email = customerReference,
                    FirstName = firstName?.Trim() is { Length: > 0 } f ? f : derivedFirst,
                    LastName = lastName?.Trim() is { Length: > 0 } l ? l : derivedLast,
                },
                cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for user '{User}'.", created.Id, customerReference);
            return created;
        }
        catch (MaxioReferenceConflictException)
        {
            // Concurrent provisioning won the race; the customer now exists - read it back.
            var raced = await _gateway.FindCustomerByReferenceAsync(customerReference, cancellationToken)
                ?? throw new MaxioApiException(409,
                    $"Maxio reported customer reference '{customerReference}' as already taken, but the customer could not be read back.");

            _logger.LogWarning("Customer for user '{User}' already existed (created concurrently): {CustomerId}.",
                customerReference, raced.Id);
            return raced;
        }
    }

    public static string BuildSubscriptionReference(string customerReference, string planHandle) =>
        $"{SUBSCRIPTION_REFERENCE_PREFIX}:{customerReference}:{planHandle}";

    private static (string FirstName, string LastName) DeriveCustomerName(string email)
    {
        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        return string.IsNullOrWhiteSpace(localPart) ? ("eShop", "Customer") : (localPart, "eShopOnWeb");
    }

    private static void GuardAgainstBlank(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} must not be blank.", paramName);
    }
}
