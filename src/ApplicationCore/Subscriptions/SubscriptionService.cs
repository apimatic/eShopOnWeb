using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Default <see cref="ISubscriptionService"/> implementation. Contains the idempotency rules for
/// the subscribe flow; it is deliberately gateway-agnostic (depends only on <see cref="IMaxioGateway"/>).
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    // Maxio states that mean the subscription is not usable and should not block re-subscribing.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended",
        "unpaid",
    };

    private readonly IMaxioGateway _gateway;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly KeyedAsyncLock _subscribeLock;

    public SubscriptionService(IMaxioGateway gateway, IAppLogger<SubscriptionService> logger, KeyedAsyncLock subscribeLock)
    {
        _gateway = gateway;
        _logger = logger;
        _subscribeLock = subscribeLock;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
        => _gateway.ListPlansAsync(cancellationToken);

    public async Task<SubscriptionResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        // 1. Resolve the requested plan against the configured product family. Falling back to the
        //    first advertised plan keeps the flow frictionless without hard-coding any handle.
        var plans = await _gateway.ListPlansAsync(cancellationToken);
        var plan = ResolvePlan(plans, command.ProductHandle);

        // Serialize concurrent subscribe attempts for the same user so a double-click cannot
        // interleave the "already subscribed?" check with the create and produce a duplicate.
        using (await _subscribeLock.LockAsync(command.Customer.Reference, cancellationToken))
        {
            // 2. Ensure a Maxio customer exists for this user (idempotent by reference).
            var customer = await EnsureCustomerAsync(command.Customer, cancellationToken);

            // 3. Idempotency: if the user already has a live subscription to this plan, return it.
            var existing = await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var live = FindLiveSubscription(existing, plan.Handle);
            if (live is not null)
            {
                _logger.LogInformation(
                    "User {0} already has live subscription {1} to plan {2}; returning existing.",
                    customer.Reference ?? customer.Id.ToString(), live.Id, plan.Handle);
                return new SubscriptionResult(live, AlreadyExisted: true);
            }

            // 4. Create the subscription.
            try
            {
                var created = await _gateway.CreateSubscriptionAsync(
                    new NewSubscription(customer.Id, plan.Handle, UniquenessToken: null), cancellationToken);
                _logger.LogInformation(
                    "Created subscription {0} ({1}) for customer {2} on plan {3}.",
                    created.Id, created.State, customer.Id, plan.Handle);
                return new SubscriptionResult(created, AlreadyExisted: false);
            }
            catch (MaxioApiException ex) when (ex.IsDuplicate)
            {
                // Defensive net for a cross-instance race: re-read and return whatever exists.
                _logger.LogWarning(
                    "Duplicate subscribe detected for customer {0} on plan {1}; re-reading existing subscription.",
                    customer.Id, plan.Handle);
                var after = await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
                var match = FindLiveSubscription(after, plan.Handle)
                            ?? after.Where(s => HandleMatches(s.ProductHandle, plan.Handle))
                                    .OrderByDescending(s => s.CreatedAt)
                                    .FirstOrDefault();
                if (match is not null)
                {
                    return new SubscriptionResult(match, AlreadyExisted: true);
                }

                throw;
            }
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customer = await _gateway.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(CustomerRegistration registration, CancellationToken cancellationToken)
    {
        var existing = await _gateway.FindCustomerByReferenceAsync(registration.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _gateway.CreateCustomerAsync(registration, cancellationToken);
            _logger.LogInformation("Created Maxio customer {0} for reference {1}.", created.Id, registration.Reference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsDuplicate)
        {
            // A concurrent request created the customer with the same reference between our
            // lookup and create. Re-fetch and use the now-existing record.
            _logger.LogWarning("Customer reference {0} already taken; re-reading existing customer.", registration.Reference);
            var afterRace = await _gateway.FindCustomerByReferenceAsync(registration.Reference, cancellationToken);
            if (afterRace is not null)
            {
                return afterRace;
            }

            throw;
        }
    }

    private SubscriptionPlan ResolvePlan(IReadOnlyList<SubscriptionPlan> plans, string? requestedHandle)
    {
        if (!string.IsNullOrWhiteSpace(requestedHandle))
        {
            var match = plans.FirstOrDefault(p => HandleMatches(p.Handle, requestedHandle));
            if (match is null)
            {
                throw new PlanNotFoundException(requestedHandle);
            }

            return match;
        }

        var fallback = plans.FirstOrDefault();
        if (fallback is null)
        {
            throw new PlanNotFoundException("(none available)");
        }

        _logger.LogInformation("No plan handle supplied; defaulting to first available plan '{0}'.", fallback.Handle);
        return fallback;
    }

    private static CustomerSubscription? FindLiveSubscription(IReadOnlyList<CustomerSubscription> subscriptions, string planHandle)
        => subscriptions.FirstOrDefault(s => HandleMatches(s.ProductHandle, planHandle) && !TerminalStates.Contains(s.State));

    private static bool HandleMatches(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
