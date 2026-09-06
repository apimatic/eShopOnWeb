using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements subscription billing against Maxio Advanced Billing, which owns the data. Nothing is
/// mirrored locally: every read goes to Maxio, and idempotency is enforced by Maxio's site-unique
/// <c>reference</c> rather than by application-side bookkeeping that a restart would lose.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private readonly IMaxioApiClient _client;
    private readonly IMaxioCatalogCache _catalog;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        IMaxioCatalogCache catalog,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _catalog = catalog;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        return await _catalog.GetPlansAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();

        var plan = await ResolvePlanAsync(request.PlanHandle, cancellationToken).ConfigureAwait(false);
        var (customer, customerCreated) = await EnsureCustomerAsync(request.Subscriber, cancellationToken).ConfigureAwait(false);
        var currency = await _catalog.GetCurrencyAsync(cancellationToken).ConfigureAwait(false);

        var customerReference = MaxioReference.ForCustomer(_settings.ReferencePrefix, request.Subscriber.UserName);
        var scope = MaxioReference.ScopeFor(plan.Handle, request.IdempotencyKey);

        // An explicit idempotency key pins the request to exactly one reference. Without one, the
        // plan handle is the scope, and later slots are only reached when earlier ones hold
        // subscriptions that have already ended - i.e. the shopper is re-subscribing.
        var maxAttempts = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? _settings.MaxReferenceAttempts : 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var reference = MaxioReference.ForSubscription(customerReference, scope, attempt);

            var existing = await _client.FindSubscriptionByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                if (TryReuse(existing, customer.Id, reference, currency, customerCreated, out var reused))
                {
                    return reused;
                }

                continue;
            }

            var created = await TryCreateAsync(plan.Handle, customer.Id, reference, cancellationToken).ConfigureAwait(false);
            if (created is not null)
            {
                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} ({Reference}) for customer {CustomerId} on plan {PlanHandle}.",
                    created.Id,
                    reference,
                    customer.Id,
                    plan.Handle);

                return new SubscribeResult(MaxioMapper.ToSubscription(created, currency), AlreadySubscribed: false, customerCreated);
            }

            // The reference was taken between the lookup and the create - a concurrent double submit.
            // Whoever won the race owns it, so read it back and reuse it.
            var raced = await _client.FindSubscriptionByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
            if (raced is not null && TryReuse(raced, customer.Id, reference, currency, customerCreated, out var racedResult))
            {
                return racedResult;
            }
        }

        throw new BillingProviderException(
            $"Could not allocate a subscription reference for plan '{plan.Handle}' after {maxAttempts} attempt(s). " +
            "The shopper already holds that many ended subscriptions to this plan.",
            isClientError: true);
    }

    public async Task<IReadOnlyList<Subscription>> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        EnsureConfigured();

        var reference = MaxioReference.ForCustomer(_settings.ReferencePrefix, subscriber.UserName);
        var customer = await _client.ReadCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        var currency = await _catalog.GetCurrencyAsync(cancellationToken).ConfigureAwait(false);
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);

        return subscriptions
            .Select(subscription => MaxioMapper.ToSubscription(subscription, currency))
            .OrderByDescending(subscription => subscription.CreatedAt)
            .ToList();
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await _catalog.GetPlansAsync(cancellationToken).ConfigureAwait(false);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase));

        return plan ?? throw new SubscriptionPlanNotFoundException(planHandle, _settings.ProductFamilyHandle!);
    }

    /// <summary>
    /// Looks the shopper's Maxio customer up by reference and creates it only if it is missing.
    /// A create that loses the race fails with "Reference: must be unique", which is resolved by
    /// reading the winner back - so concurrent first-time subscribes still yield a single customer.
    /// </summary>
    private async Task<(MaxioCustomer Customer, bool Created)> EnsureCustomerAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var reference = MaxioReference.ForCustomer(_settings.ReferencePrefix, subscriber.UserName);

        var existing = await _client.ReadCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return (existing, false);
        }

        var (firstName, lastName) = MaxioCustomerName.Resolve(subscriber);

        try
        {
            var created = await _client.CreateCustomerAsync(
                new MaxioCreateCustomerRequest
                {
                    Customer = new MaxioCreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = subscriber.Email,
                        Reference = reference
                    }
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Created Maxio customer {CustomerId} ({Reference}).", created.Id, reference);
            return (created, true);
        }
        catch (MaxioApiException ex) when (ex.IsReferenceTaken)
        {
            _logger.LogInformation("Maxio customer {Reference} was created concurrently; reusing it.", reference);

            var winner = await _client.ReadCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
            return winner is not null
                ? (winner, false)
                : throw new BillingProviderException(
                    "The billing provider reports the customer reference is taken but does not return the customer.",
                    isClientError: false,
                    errors: ex.Errors,
                    innerException: ex);
        }
    }

    /// <summary>
    /// Creates the subscription, returning <see langword="null"/> when the reference was taken in the
    /// meantime so the caller can reconcile.
    /// </summary>
    private async Task<MaxioSubscription?> TryCreateAsync(string planHandle, int customerId, string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.CreateSubscriptionAsync(
                new MaxioCreateSubscriptionRequest
                {
                    Subscription = new MaxioCreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerId = customerId,
                        Reference = reference,
                        PaymentCollectionMethod = string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod)
                            ? null
                            : _settings.PaymentCollectionMethod
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (MaxioApiException ex) when (ex.IsReferenceTaken)
        {
            return null;
        }
    }

    /// <summary>
    /// Decides whether a subscription found at a reference can be handed back as the result. An ended
    /// subscription frees its slot for a re-subscribe; anything belonging to another customer is left
    /// alone, which can only happen if the reference scheme was changed under a live site.
    /// </summary>
    private bool TryReuse(
        MaxioSubscription candidate,
        int customerId,
        string reference,
        string currency,
        bool customerCreated,
        out SubscribeResult result)
    {
        result = default!;

        if (candidate.Customer is not null && candidate.Customer.Id != customerId)
        {
            _logger.LogWarning(
                "Maxio subscription reference {Reference} belongs to customer {OwnerId}, not {CustomerId}; skipping it.",
                reference,
                candidate.Customer.Id,
                customerId);

            return false;
        }

        var mapped = MaxioMapper.ToSubscription(candidate, currency);
        if (!mapped.IsLive)
        {
            _logger.LogInformation(
                "Maxio subscription {SubscriptionId} at {Reference} has ended ({State}); trying the next reference.",
                mapped.Id,
                reference,
                mapped.RawState);

            return false;
        }

        _logger.LogInformation(
            "Reusing existing Maxio subscription {SubscriptionId} ({Reference}) in state {State}.",
            mapped.Id,
            reference,
            mapped.RawState);

        result = new SubscribeResult(mapped, AlreadySubscribed: true, customerCreated);
        return true;
    }

    private void EnsureConfigured()
    {
        var problems = _settings.Validate();
        if (problems.Count > 0)
        {
            throw new BillingNotConfiguredException(problems);
        }
    }
}
