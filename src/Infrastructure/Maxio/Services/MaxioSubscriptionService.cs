using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Services;

/// <summary>
/// Enrolls eShopOnWeb shoppers onto Maxio plans and reports what they hold.
/// </summary>
/// <remarks>
/// <para>Subscribing is idempotent, defended at three levels:</para>
/// <list type="number">
/// <item>
/// A per-shopper lock serialises concurrent subscribe requests inside this process, so the
/// check-then-create sequence is never interleaved with itself. This is what makes a double-click
/// harmless.
/// </item>
/// <item>
/// Before creating anything, the shopper's existing subscriptions are read. A subscription the
/// shopper still holds on the same plan is returned as-is rather than duplicated.
/// </item>
/// <item>
/// The new subscription carries a reference derived deterministically from the shopper and the
/// plan. Maxio enforces uniqueness on it, so a request that slips past the first two levels - a
/// second instance of the app, a retry after a dropped response - is refused by the provider, and
/// that refusal is resolved back to the subscription that already exists.
/// </item>
/// </list>
/// <para>
/// Cancelling and subscribing again is still possible: only subscriptions in an occupied state
/// block a new signup, and a spent reference is superseded rather than reused.
/// </para>
/// </remarks>
public class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>How many reference variants to try before giving up on finding a free one.</summary>
    private const int MaxReferenceAttempts = 50;

    private readonly IMaxioApiClient _client;
    private readonly ISubscriptionPlanCatalog _planCatalog;
    private readonly KeyedAsyncLock _subscriberLocks;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        ISubscriptionPlanCatalog planCatalog,
        KeyedAsyncLock subscriberLocks,
        IOptionsMonitor<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _planCatalog = planCatalog;
        _subscriberLocks = subscriberLocks;
        _options = options;
        _logger = logger;
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscribeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var options = GetValidatedOptions();

        var plan = await _planCatalog.FindPlanAsync(command.PlanHandle, cancellationToken)
                   ?? throw new SubscriptionPlanNotFoundException(command.PlanHandle);

        var customerReference = MaxioReferences.ForCustomer(options.ReferencePrefix, command.Subscriber.Email);

        using (await _subscriberLocks.AcquireAsync(customerReference, cancellationToken))
        {
            var (customer, customerCreated) =
                await EnsureCustomerAsync(command.Subscriber, customerReference, cancellationToken);

            var existing = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

            var desiredReference =
                MaxioReferences.ForSubscription(customerReference, plan.Handle, command.IdempotencyKey);

            // A caller-supplied key promises that the same key always means the same subscription,
            // so an exact reference match short-circuits even when that subscription has since
            // ended. Without a key there is no such promise: an ended subscription must not stop
            // the shopper subscribing again, so the match below is skipped and the occupancy check
            // decides instead.
            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                var replay = existing.FirstOrDefault(subscription =>
                    string.Equals(subscription.Reference, desiredReference, StringComparison.Ordinal));

                if (replay is not null)
                {
                    _logger.LogInformation(
                        "Subscribe replayed for customer {CustomerId} with an idempotency key: returning existing subscription {SubscriptionId}.",
                        customer.Id, replay.Id);

                    return Result(replay, created: false, customerCreated, customerReference, plan);
                }
            }

            var held = existing.FirstOrDefault(subscription =>
                string.Equals(subscription.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase)
                && MaxioMapper.ParseState(subscription.State).IsOccupied());

            if (held is not null)
            {
                _logger.LogInformation(
                    "Customer {CustomerId} already holds subscription {SubscriptionId} on plan '{PlanHandle}'; not creating another.",
                    customer.Id, held.Id, plan.Handle);

                return Result(held, created: false, customerCreated, customerReference, plan);
            }

            var reference = NextFreeReference(desiredReference, existing);

            var created = await CreateSubscriptionAsync(customer.Id, plan.Handle, reference, options, cancellationToken);

            return Result(created.Subscription, created.Created, customerCreated, customerReference, plan);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        SubscriberProfile subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var options = GetValidatedOptions();
        var customerReference = MaxioReferences.ForCustomer(options.ReferencePrefix, subscriber.Email);

        var customer = await _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            // The shopper has never subscribed, so no billing customer exists yet. That is a normal
            // empty result, not an error.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var currency = await FallbackCurrencyAsync(cancellationToken);

        return subscriptions
            .OrderByDescending(MaxioMapper.SortKey)
            .ThenByDescending(subscription => subscription.Id)
            .Select(subscription => MaxioMapper.ToSubscription(subscription, currency))
            .ToList();
    }

    private async Task<(MaxioCustomer Customer, bool Created)> EnsureCustomerAsync(
        SubscriberProfile subscriber,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return (existing, false);
        }

        var payload = new MaxioCreateCustomer
        {
            FirstName = subscriber.FirstName,
            LastName = subscriber.LastName,
            Email = subscriber.Email,
            Organization = subscriber.Organization,
            Reference = customerReference
        };

        try
        {
            var created = await _client.CreateCustomerAsync(payload, cancellationToken);

            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for reference {CustomerReference}.",
                created.Id, customerReference);

            return (created, true);
        }
        catch (BillingProviderException ex) when (IsReferenceTaken(ex))
        {
            // Something else created the customer between the lookup and the create. Maxio refused
            // the duplicate, which is exactly the outcome we want; adopt the existing record.
            _logger.LogInformation(
                "Maxio customer for reference {CustomerReference} already existed; adopting it.",
                customerReference);

            var raced = await _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            return (raced, false);
        }
    }

    private async Task<(MaxioSubscription Subscription, bool Created)> CreateSubscriptionAsync(
        int customerId,
        string planHandle,
        string reference,
        MaxioOptions options,
        CancellationToken cancellationToken)
    {
        var payload = new MaxioCreateSubscription
        {
            CustomerId = customerId,
            ProductHandle = planHandle,
            Reference = reference,
            PaymentCollectionMethod = options.PaymentCollectionMethod
        };

        try
        {
            var subscription = await _client.CreateSubscriptionAsync(payload, cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} on plan '{PlanHandle}' for customer {CustomerId}.",
                subscription.Id, planHandle, customerId);

            return (subscription, true);
        }
        catch (BillingProviderException ex) when (IsReferenceTaken(ex))
        {
            // The reference was claimed after we picked it, which means the subscription this
            // request was trying to create already exists. Resolve it instead of creating a second.
            _logger.LogInformation(
                "Maxio subscription reference {Reference} was already taken; resolving the existing subscription.",
                reference);

            var existing = await _client.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return (existing, false);
        }
    }

    /// <summary>
    /// Picks a reference that is not already spent by one of the shopper's subscriptions.
    /// </summary>
    /// <remarks>
    /// Maxio enforces subscription reference uniqueness for the lifetime of the site, so a shopper
    /// who cancels and subscribes again to the same plan needs a new one. References always embed
    /// the customer reference, so only this shopper's own records can collide.
    /// </remarks>
    private static string NextFreeReference(string desiredReference, IReadOnlyList<MaxioSubscription> existing)
    {
        var taken = existing
            .Select(subscription => subscription.Reference)
            .Where(reference => !string.IsNullOrEmpty(reference))
            .ToHashSet(StringComparer.Ordinal);

        if (!taken.Contains(desiredReference))
        {
            return desiredReference;
        }

        for (var sequence = 2; sequence <= MaxReferenceAttempts; sequence++)
        {
            var candidate = MaxioReferences.WithSequence(desiredReference, sequence);
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new BillingProviderException(
            "Could not allocate a unique subscription reference for this shopper and plan.");
    }

    private SubscribeResult Result(
        MaxioSubscription subscription,
        bool created,
        bool customerCreated,
        string customerReference,
        SubscriptionPlan plan) =>
        new(MaxioMapper.ToSubscription(subscription, plan.Currency), created, customerCreated, customerReference);

    /// <summary>
    /// Currency to fall back on when a subscription record carries none. Read from the plan catalog
    /// so it comes from the Maxio site rather than from a constant.
    /// </summary>
    private async Task<string> FallbackCurrencyAsync(CancellationToken cancellationToken)
    {
        var plans = await _planCatalog.ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault()?.Currency ?? "USD";
    }

    /// <summary>
    /// Recognises the provider refusing a write because the reference is already in use.
    /// </summary>
    /// <remarks>
    /// Maxio answers 422 with a message of the form
    /// "Reference: must be unique - that value has been taken."
    /// </remarks>
    internal static bool IsReferenceTaken(BillingProviderException exception) =>
        exception.ProviderStatusCode == 422
        && exception.ProviderErrors.Any(error =>
            error.Contains("reference", StringComparison.OrdinalIgnoreCase)
            && (error.Contains("unique", StringComparison.OrdinalIgnoreCase)
                || error.Contains("taken", StringComparison.OrdinalIgnoreCase)));

    private MaxioOptions GetValidatedOptions()
    {
        var options = _options.CurrentValue;
        var problems = options.Validate().ToList();

        if (problems.Count > 0)
        {
            throw new BillingConfigurationException(
                "Subscription billing is not configured: " + string.Join(" ", problems));
        }

        return options;
    }
}
