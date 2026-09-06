using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscribe flow on top of <see cref="IBillingGateway"/>.
/// </summary>
/// <remarks>
/// <para>Idempotency is layered, because no single layer covers every retry shape:</para>
/// <list type="number">
/// <item>an in-process lock keyed on the user serialises a double-click on one instance;</item>
/// <item>the customer is looked up by its stable reference before it is created, and a lost
/// create race is recovered by re-reading the customer that won it;</item>
/// <item>an existing live subscription to the same plan is returned as-is instead of
/// enrolling the shopper twice;</item>
/// <item>when the caller supplies an idempotency key it becomes the Maxio subscription
/// reference, which Maxio enforces as unique site-wide - so even two instances racing
/// each other end up with exactly one subscription.</item>
/// </list>
/// </remarks>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingGateway _billingGateway;
    private readonly KeyedAsyncLock _subscribeLock;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(
        IBillingGateway billingGateway,
        KeyedAsyncLock subscribeLock,
        IAppLogger<SubscriptionService> logger)
    {
        _billingGateway = billingGateway;
        _subscribeLock = subscribeLock;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        _billingGateway.ListPlansAsync(cancellationToken);

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var reference = BillingCustomerReference.ForUser(userName);
        var customer = await _billingGateway.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _billingGateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(s => s.Id)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscribeCommand command,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(command, nameof(command));
        Guard.Against.NullOrWhiteSpace(command.UserName, nameof(command.UserName));
        Guard.Against.NullOrWhiteSpace(command.PlanHandle, nameof(command.PlanHandle));

        var customerReference = BillingCustomerReference.ForUser(command.UserName);

        using (await _subscribeLock.AcquireAsync(customerReference, cancellationToken))
        {
            var plan = await _billingGateway.FindPlanAsync(command.PlanHandle, cancellationToken)
                       ?? throw new SubscriptionPlanNotFoundException(command.PlanHandle);

            if (plan.RequiresPaymentMethod)
            {
                throw new PaymentMethodRequiredException(plan.Handle);
            }

            var customer = await EnsureCustomerAsync(command, customerReference, cancellationToken);

            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Subscribe for {0} to {1} is a no-op; subscription {2} is already {3}.",
                    customerReference, plan.Handle, existing.Id, existing.State);

                return new SubscribeResult { Subscription = existing, AlreadySubscribed = true };
            }

            var subscriptionReference = BuildSubscriptionReference(customerReference, command.IdempotencyKey);
            var newSubscription = new NewSubscription
            {
                CustomerId = customer.Id,
                PlanHandle = plan.Handle,

                // Remittance, not automatic: we got here only because the plan does not
                // require a payment method, so there is no payment profile to charge and
                // Maxio would reject an automatic signup with "no payment method on file".
                PaymentCollectionMethod = PaymentCollectionMethods.Remittance,
                Reference = subscriptionReference
            };

            try
            {
                var created = await _billingGateway.CreateSubscriptionAsync(newSubscription, cancellationToken);

                _logger.LogInformation(
                    "Created subscription {0} ({1}) for {2} on plan {3}.",
                    created.Id, created.State, customerReference, plan.Handle);

                return new SubscribeResult { Subscription = created, AlreadySubscribed = false };
            }
            catch (BillingGatewayException ex) when (ex.IsDuplicateReference && subscriptionReference is not null)
            {
                // Another request using the same idempotency key got there first.
                var winner = await _billingGateway.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (winner is null)
                {
                    throw;
                }

                _logger.LogInformation(
                    "Subscribe for {0} lost an idempotency race; returning subscription {1}.",
                    customerReference, winner.Id);

                return new SubscribeResult { Subscription = winner, AlreadySubscribed = true };
            }
        }
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(
        SubscribeCommand command,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await _billingGateway.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = BillingCustomerNaming.Derive(command.UserName, command.FirstName, command.LastName);
        var newCustomer = new NewBillingCustomer
        {
            Reference = customerReference,
            Email = command.UserName,
            FirstName = firstName,
            LastName = lastName
        };

        try
        {
            var created = await _billingGateway.CreateCustomerAsync(newCustomer, cancellationToken);
            _logger.LogInformation("Created billing customer {0} for {1}.", created.Id, customerReference);
            return created;
        }
        catch (BillingGatewayException ex) when (ex.IsDuplicateReference)
        {
            // Somebody created the customer between our lookup and our create.
            var winner = await _billingGateway.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            _logger.LogInformation(
                "Billing customer {0} already existed for {1}; reusing it.", winner.Id, customerReference);
            return winner;
        }
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _billingGateway.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions
            .Where(s => s.IsLive && string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(s => s.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// Turns a caller idempotency key into a Maxio subscription reference. Hashing keeps the
    /// reference a fixed, safe length and keeps the shopper email out of the billing
    /// system reference field, while staying deterministic across instances and restarts.
    /// </summary>
    private static string? BuildSubscriptionReference(string customerReference, string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        var material = Encoding.UTF8.GetBytes($"{customerReference}|{idempotencyKey!.Trim()}");
        var hash = SHA256.HashData(material);
        return "eshop-sub-" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
