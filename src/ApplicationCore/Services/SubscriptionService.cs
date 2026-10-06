using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates subscription flows against Maxio Advanced Billing. The billing
/// system is the system of record: no local subscription rows are kept, so
/// consistency comes from deterministic external ids (references) rather than
/// a second store that could drift.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    /// <summary>
    /// Prefix tying every customer reference this app creates to the app,
    /// avoiding collisions with other integrations on the same Maxio site.
    /// </summary>
    public const string CUSTOMER_REFERENCE_PREFIX = "eshop-user-";

    /// <summary>
    /// Prefix for subscription references: {prefix}{userId}-{planHandle}.
    /// </summary>
    public const string SUBSCRIPTION_REFERENCE_PREFIX = "eshop-sub-";

    /// <summary>
    /// Billing API payment collection mode for invoice-based billing
    /// (no automatic card charge), used for plans that do not require a
    /// payment method.
    /// </summary>
    public const string PAYMENT_COLLECTION_REMITTANCE = "remittance";

    private static readonly string[] SubscriptionOccupyingStates =
    {
        MaxioSubscriptionStates.Pending,
        MaxioSubscriptionStates.Trialing,
        MaxioSubscriptionStates.Assessing,
        MaxioSubscriptionStates.Active,
        MaxioSubscriptionStates.SoftFailure,
        MaxioSubscriptionStates.PastDue,
        MaxioSubscriptionStates.Suspended,
        MaxioSubscriptionStates.Paused,
        MaxioSubscriptionStates.Unpaid,
        MaxioSubscriptionStates.OnHold,
        MaxioSubscriptionStates.AwaitingSignup,
    };

    private readonly IMaxioBillingGateway _gateway;
    private readonly MaxioSettings _settings;

    public SubscriptionService(IMaxioBillingGateway gateway, IOptions<MaxioSettings> settings)
    {
        _gateway = gateway;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                $"'{MaxioSettings.CONFIG_NAME}:{nameof(MaxioSettings.ProductFamilyHandle)}' is not configured.");
        }

        return await _gateway.GetFamilyPlansAsync(_settings.ProductFamilyHandle, cancellationToken);
    }

    public async Task<SubscriptionProvisioningResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber);
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        // Validating the handle against the configured family also keeps the
        // blast radius of a typo/malicious handle inside our own catalog.
        var plans = await GetAvailablePlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

        var subscriptions = await _gateway.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var active = FindActiveSubscriptionFor(subscriptions, plan.Id);
        if (active is not null)
        {
            return new SubscriptionProvisioningResult(active, alreadySubscribed: true);
        }

        var createRequest = new CreateMaxioSubscriptionRequest
        {
            CustomerId = customer.Id,
            ProductId = plan.Id,
            Reference = BuildSubscriptionReference(subscriber.UserId, plan.Handle),
            UniquenessToken = Guid.NewGuid().ToString("N"),
            // Catalog-driven payment handling: plans flagged as not requiring a
            // payment method are enrolled with invoice-based collection, so no
            // card capture / 3DS is needed at signup. Otherwise fall back to the
            // site default (automatic), where the billing system itself rejects
            // enrollments lacking a payment profile.
            PaymentCollectionMethod = plan.RequiresPaymentMethod ? null : PAYMENT_COLLECTION_REMITTANCE,
        };

        try
        {
            var created = await _gateway.CreateSubscriptionAsync(createRequest, cancellationToken);
            return new SubscriptionProvisioningResult(created, alreadySubscribed: false);
        }
        catch (MaxioApiException ex) when (ex.StatusCode is 409 or 422)
        {
            // We lost a race with a concurrent identical request. The winner's
            // subscription exists by now, so return it instead of failing.
            var raced = FindActiveSubscriptionFor(
                await _gateway.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken), plan.Id);

            if (raced is not null)
            {
                return new SubscriptionProvisioningResult(raced, alreadySubscribed: true);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));

        var customer = await _gateway.FindCustomerByReferenceAsync(
            BuildCustomerReference(userId), cancellationToken);

        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _gateway.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(subscriber.UserId);

        var existing = await _gateway.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new CreateMaxioCustomerRequest
        {
            Reference = reference,
            Email = subscriber.Email,
            FirstName = subscriber.FirstName ?? DeriveFirstNameFrom(subscriber.Email),
            LastName = subscriber.LastName ?? "Shopper",
            Organization = "eShopOnWeb",
        };

        try
        {
            return await _gateway.CreateCustomerAsync(request, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode is 409 or 422)
        {
            // The reference is unique per site: if creation was rejected as a
            // duplicate, another (concurrent) request already provisioned the
            // customer. Re-read it so a double-click never yields two records.
            var raced = await _gateway.FindCustomerByReferenceAsync(reference, cancellationToken);

            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private static MaxioSubscription? FindActiveSubscriptionFor(
        IReadOnlyList<MaxioSubscription> subscriptions,
        long planId) =>
        subscriptions.FirstOrDefault(s =>
            s.ProductId == planId &&
            !s.IsTerminated &&
            SubscriptionOccupyingStates.Contains(s.State));

    private static string BuildCustomerReference(string userId) =>
        $"{CUSTOMER_REFERENCE_PREFIX}{userId}";

    private static string BuildSubscriptionReference(string userId, string planHandle) =>
        $"{SUBSCRIPTION_REFERENCE_PREFIX}{userId}-{planHandle}";

    private static string DeriveFirstNameFrom(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }
}
