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
/// Implements the subscription capability on top of Maxio Advanced Billing.
/// <para>
/// Maxio is the system of record: nothing is mirrored into the eShopOnWeb database, so the flow
/// behaves identically whether the app runs on SQL Server or the in-memory provider, and survives a
/// restart. Every read and write goes through <see cref="IMaxioApiClient"/>, which speaks only the
/// operations defined in the OpenAPI specification.
/// </para>
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>
    /// How many reference variants to try when a shopper re-subscribes to a plan they have held
    /// before. Each previous signup consumed "customer:plan", "customer:plan:2", and so on.
    /// </summary>
    private const int MaxSubscriptionReferenceAttempts = 25;

    /// <summary>Static so concurrent requests collapse regardless of this service's DI lifetime.</summary>
    private static readonly StripedAsyncLock _subscribeLocks = new();

    private readonly IMaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForProductFamilyAsync(ProductFamilySelector, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MaxioMapper.ToSubscriptionPlan)
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = await FindPlanAsync(request.PlanHandle, cancellationToken)
                   ?? throw new SubscriptionPlanNotFoundException(request.PlanHandle);

        if (plan.RequiresPaymentMethod)
        {
            throw new PaymentMethodRequiredException(plan.Handle);
        }

        var customerReference = MaxioReferences.ForCustomer(_options.ReferencePrefix, request.Subscriber.UserName);

        // Collapse a double-click before it reaches Maxio. Cross-process safety does not rely on
        // this: the unique-reference handling below covers that case too.
        using var _ = await _subscribeLocks.AcquireAsync(customerReference, cancellationToken);

        var customer = await EnsureCustomerAsync(request.Subscriber, customerReference, cancellationToken);
        var existing = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        if (request.IdempotencyKey is not null)
        {
            var keyedReference = MaxioReferences.ForSubscription(customerReference, request.IdempotencyKey);
            var replay = await FindByReferenceAsync(existing, keyedReference, cancellationToken);

            if (replay is not null)
            {
                _logger.LogInformation(
                    "Subscribe replayed for {CustomerReference} under idempotency key; returning subscription {SubscriptionId}.",
                    customerReference, replay.Id);

                return new SubscribeResult(MaxioMapper.ToCustomerSubscription(replay), SubscribeOutcome.IdempotentReplay);
            }

            return await CreateSubscriptionAsync(plan, customer, keyedReference, customerReference: null, cancellationToken);
        }

        var live = existing.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
            SubscriptionStates.IsLive(s.State));

        if (live is not null)
        {
            _logger.LogInformation(
                "Customer {CustomerReference} already holds live subscription {SubscriptionId} for plan {PlanHandle}; not creating another.",
                customerReference, live.Id, plan.Handle);

            return new SubscribeResult(MaxioMapper.ToCustomerSubscription(live), SubscribeOutcome.AlreadySubscribed);
        }

        var (reference, attempt) = NextAvailableReference(existing, customerReference, plan.Handle);

        return await CreateSubscriptionAsync(plan, customer, reference, customerReference, cancellationToken, attempt);
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var customerReference = MaxioReferences.ForCustomer(_options.ReferencePrefix, subscriber.UserName);
        var customer = await _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken);

        if (customer is null)
        {
            // The shopper has never subscribed, so no billing customer exists yet. Not an error.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .Select(MaxioMapper.ToCustomerSubscription)
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(s => s.Id)
            .ToList();
    }

    /// <summary>
    /// Addresses the configured product family by handle, using the specification's
    /// "<c>handle:</c>"-prefixed path segment, so numeric ids never leak into configuration.
    /// </summary>
    private string ProductFamilySelector => $"handle:{_options.ProductFamilyHandle}";

    /// <summary>
    /// Resolves a plan from the configured family rather than reading the product by handle directly,
    /// so a caller can only ever subscribe to something this site actually offers.
    /// </summary>
    private async Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);

        return plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Looks up the shopper's Maxio customer and creates it when missing. If two callers race, the
    /// loser's create fails on the unique reference and re-reads the winner's record.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(
        SubscriberIdentity subscriber,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = subscriber.ResolvedFirstName,
                LastName = subscriber.ResolvedLastName,
                Email = subscriber.Email,
                Reference = customerReference
            }
        };

        try
        {
            var created = await _client.CreateCustomerAsync(request, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for {CustomerReference}.",
                created.Id, customerReference);

            return created;
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateReference)
        {
            _logger.LogInformation(
                "Maxio customer {CustomerReference} was created concurrently; using the existing record.", customerReference);

            return await _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken)
                   ?? throw new MaxioApiException(
                       $"Maxio reported customer reference '{customerReference}' as taken but did not return it.",
                       ex.StatusCode, ex.ProviderErrors, ex);
        }
    }

    /// <summary>
    /// Creates the subscription, resolving a reference collision rather than failing on it.
    /// <para>
    /// <paramref name="customerReference"/> is supplied only for the plan-derived reference scheme;
    /// passing <c>null</c> (the caller-supplied idempotency key scheme) means a collision can only
    /// ever be a replay of the very same request, so the existing subscription is returned as-is.
    /// </para>
    /// </summary>
    private async Task<SubscribeResult> CreateSubscriptionAsync(
        SubscriptionPlan plan,
        MaxioCustomer customer,
        string reference,
        string? customerReference,
        CancellationToken cancellationToken,
        int attempt = 1)
    {
        var allowReferenceRetry = customerReference is not null;

        while (true)
        {
            var request = new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    Reference = reference,
                    // The demo plans carry no stored payment method, so bill by invoice rather than
                    // attempting an automatic capture that would fail with "no payment method on file".
                    PaymentCollectionMethod = _options.PaymentCollectionMethod
                }
            };

            try
            {
                var created = await _client.CreateSubscriptionAsync(request, cancellationToken);

                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} ({Reference}) for customer {CustomerId} on plan {PlanHandle}; state {State}.",
                    created.Id, reference, customer.Id, plan.Handle, created.State);

                return new SubscribeResult(MaxioMapper.ToCustomerSubscription(created), SubscribeOutcome.Created);
            }
            catch (MaxioApiException ex) when (ex.IsDuplicateReference)
            {
                // Another request already used this reference. Read it back: if it is still live the
                // shopper is subscribed and we are done, otherwise move on to the next reference.
                var owner = await _client.FindSubscriptionByReferenceAsync(reference, cancellationToken);

                if (owner is not null && (!allowReferenceRetry || SubscriptionStates.IsLive(owner.State)))
                {
                    _logger.LogInformation(
                        "Subscription reference {Reference} was already taken by subscription {SubscriptionId}; returning it.",
                        reference, owner.Id);

                    return new SubscribeResult(
                        MaxioMapper.ToCustomerSubscription(owner),
                        allowReferenceRetry ? SubscribeOutcome.AlreadySubscribed : SubscribeOutcome.IdempotentReplay);
                }

                if (!allowReferenceRetry || ++attempt > MaxSubscriptionReferenceAttempts)
                {
                    throw;
                }

                reference = MaxioReferences.ForSubscription(customerReference!, plan.Handle, attempt);
            }
        }
    }

    /// <summary>
    /// Picks the first "<c>{customer}:{plan}</c>" reference variant no existing subscription owns.
    /// Uses the list already fetched for this customer, so it costs no extra call in the common case.
    /// </summary>
    private static (string Reference, int Attempt) NextAvailableReference(
        IReadOnlyCollection<MaxioSubscription> existing,
        string customerReference,
        string planHandle)
    {
        var taken = existing
            .Select(s => s.Reference)
            .Where(r => !string.IsNullOrEmpty(r))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        for (var attempt = 1; attempt <= MaxSubscriptionReferenceAttempts; attempt++)
        {
            var candidate = MaxioReferences.ForSubscription(customerReference, planHandle, attempt);

            if (!taken.Contains(candidate))
            {
                return (candidate, attempt);
            }
        }

        var last = MaxSubscriptionReferenceAttempts;

        return (MaxioReferences.ForSubscription(customerReference, planHandle, last), last);
    }

    private async Task<MaxioSubscription?> FindByReferenceAsync(
        IReadOnlyCollection<MaxioSubscription> known,
        string reference,
        CancellationToken cancellationToken)
    {
        var local = known.FirstOrDefault(s => string.Equals(s.Reference, reference, StringComparison.OrdinalIgnoreCase));

        return local ?? await _client.FindSubscriptionByReferenceAsync(reference, cancellationToken);
    }
}
