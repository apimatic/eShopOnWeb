using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Exceptions;
using AdvancedBilling.Standard.Models;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing, which owns the customer, the plan catalog and
/// the subscription lifecycle. eShopOnWeb persists no billing state of its own: the link between an
/// eShopOnWeb user and their Maxio customer is the customer's <c>reference</c>, derived deterministically
/// from the user key, so the integration stays correct across restarts and across instances.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// How many suffixed references to try when the natural subscription reference is already held by an
    /// ended subscription (a shopper re-subscribing after cancelling). Small on purpose: past a handful of
    /// attempts something is wrong, and failing loudly beats hammering Maxio.
    /// </summary>
    private const int MaxReferenceAttempts = 5;

    private readonly MaxioClientFactory _clientFactory;
    private readonly MaxioCatalog _catalog;
    private readonly SubscriberLockProvider _locks;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioClientFactory clientFactory,
        MaxioCatalog catalog,
        SubscriberLockProvider locks,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _clientFactory = clientFactory;
        _catalog = catalog;
        _locks = locks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var client = _clientFactory.Create();

        return await ExecuteAsync(
            () => _catalog.GetPlansAsync(client, cancellationToken),
            "list subscription plans").ConfigureAwait(false);
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _clientFactory.Options;
        var client = _clientFactory.Create();
        var subscriber = request.Subscriber;

        var plan = await ResolvePlanAsync(client, request.PlanHandle, cancellationToken).ConfigureAwait(false);

        // Serialise this shopper's subscribe attempts so a double-click does not become two round-trips
        // that each observe "no subscription yet" before either of them has created one.
        using (await _locks.AcquireAsync(subscriber.UserKey, cancellationToken).ConfigureAwait(false))
        {
            var site = await ExecuteAsync(
                () => _catalog.GetSiteAsync(client, cancellationToken),
                "read the Maxio site configuration").ConfigureAwait(false);

            var (customer, customerCreated) = await EnsureCustomerAsync(client, subscriber, cancellationToken)
                .ConfigureAwait(false);

            var existing = await FindLiveSubscriptionAsync(client, customer, plan.Handle, site, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                _logger.LogInformation(
                    "Subscriber {UserKey} is already on plan {PlanHandle} via Maxio subscription {SubscriptionId} ({State}); returning it unchanged.",
                    subscriber.UserKey, plan.Handle, existing.Id, existing.State);

                return new SubscribeResult(existing, AlreadySubscribed: true, customerCreated);
            }

            var result = await CreateSubscriptionAsync(
                client,
                subscriber,
                customer,
                plan,
                site,
                ResolveCollectionMethod(options, site),
                request.IdempotencyKey ?? plan.Handle,
                explicitIdempotencyKey: request.IdempotencyKey is not null,
                cancellationToken).ConfigureAwait(false);

            return result with { CustomerCreated = customerCreated };
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        Subscriber subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var client = _clientFactory.Create();

        var site = await ExecuteAsync(
            () => _catalog.GetSiteAsync(client, cancellationToken),
            "read the Maxio site configuration").ConfigureAwait(false);

        var customer = await FindCustomerAsync(client, subscriber, cancellationToken).ConfigureAwait(false);

        if (customer is null)
        {
            // A shopper who has never subscribed has no Maxio customer, which is not an error.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(client, customer, cancellationToken).ConfigureAwait(false);

        return subscriptions
            .Where(response => response.Subscription is not null)
            .Select(response => MaxioMapper.ToSubscription(response.Subscription, site.Currency))
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .ToArray();
    }

    /// <summary>
    /// Resolves the plan a subscribe request targets. When the request does not name one the configured
    /// default is used; failing that, a single-plan catalog is unambiguous enough to default to on its own.
    /// </summary>
    private async Task<SubscriptionPlan> ResolvePlanAsync(
        AdvancedBillingClient client,
        string? requestedHandle,
        CancellationToken cancellationToken)
    {
        var options = _clientFactory.Options;
        var handle = requestedHandle ?? options.DefaultPlanHandle;

        if (!string.IsNullOrWhiteSpace(handle))
        {
            var plan = await ExecuteAsync(
                () => _catalog.FindPlanAsync(client, handle!, cancellationToken),
                $"look up plan '{handle}'").ConfigureAwait(false);

            return plan ?? throw new SubscriptionPlanNotFoundException(handle!, options.ProductFamilyHandle!);
        }

        var plans = await ExecuteAsync(
            () => _catalog.GetPlansAsync(client, cancellationToken),
            "list subscription plans").ConfigureAwait(false);

        if (plans.Count == 1)
        {
            return plans[0];
        }

        throw new SubscriptionBillingRejectedException(
            plans.Count == 0
                ? $"Product family '{options.ProductFamilyHandle}' offers no subscription plans."
                : "This request must name the plan to subscribe to. Available plans: " +
                  string.Join(", ", plans.Select(plan => plan.Handle)) +
                  $". Alternatively, set {MaxioOptions.SectionName}:{nameof(MaxioOptions.DefaultPlanHandle)} to pick a default.");
    }

    /// <summary>
    /// Ensures a Maxio customer exists for the shopper, keyed on a deterministic reference. Maxio enforces
    /// uniqueness on that reference, so two concurrent callers cannot both create one: the loser of the
    /// race gets a 422 and reads back the winner's customer.
    /// </summary>
    private async Task<(Customer Customer, bool Created)> EnsureCustomerAsync(
        AdvancedBillingClient client,
        Subscriber subscriber,
        CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(client, subscriber, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            return (existing, false);
        }

        var (firstName, lastName) = SplitName(subscriber);

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email,
                Reference = CustomerReferenceFor(subscriber),
            },
        };

        try
        {
            var response = await ExecuteAsync(
                () => client.CustomersController.CreateCustomerAsync(request, cancellationToken),
                "create the Maxio customer",
                MaxioErrorTranslator.IsReferenceConflict).ConfigureAwait(false);

            _logger.LogInformation("Created Maxio customer {CustomerId} for subscriber {UserKey}.",
                response.Customer?.Id, subscriber.UserKey);

            return (response.Customer!, true);
        }
        catch (ApiException exception) when (MaxioErrorTranslator.IsReferenceConflict(exception))
        {
            // Another caller created the customer between our lookup and our create.
            var raced = await FindCustomerAsync(client, subscriber, cancellationToken).ConfigureAwait(false);

            if (raced is null)
            {
                throw MaxioErrorTranslator.Translate(exception, "create the Maxio customer");
            }

            _logger.LogDebug("Maxio customer {CustomerId} for subscriber {UserKey} was created concurrently.",
                raced.Id, subscriber.UserKey);

            return (raced, false);
        }
    }

    private async Task<Customer?> FindCustomerAsync(
        AdvancedBillingClient client,
        Subscriber subscriber,
        CancellationToken cancellationToken)
    {
        var reference = CustomerReferenceFor(subscriber);

        try
        {
            var response = await ExecuteAsync(
                () => client.CustomersController.ReadCustomerByReferenceAsync(reference, cancellationToken),
                "look up the Maxio customer",
                MaxioErrorTranslator.IsNotFound).ConfigureAwait(false);

            return response.Customer;
        }
        catch (ApiException exception) when (MaxioErrorTranslator.IsNotFound(exception))
        {
            return null;
        }
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(
        AdvancedBillingClient client,
        Customer customer,
        string planHandle,
        MaxioSite site,
        CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(client, customer, cancellationToken).ConfigureAwait(false);

        return subscriptions
            .Where(response => response.Subscription is not null)
            .Select(response => MaxioMapper.ToSubscription(response.Subscription, site.Currency))
            .Where(subscription => subscription.IsLive &&
                string.Equals(subscription.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    private async Task<List<SubscriptionResponse>> ListCustomerSubscriptionsAsync(
        AdvancedBillingClient client,
        Customer customer,
        CancellationToken cancellationToken)
    {
        if (customer.Id is null)
        {
            return new List<SubscriptionResponse>();
        }

        try
        {
            return await ExecuteAsync(
                () => client.CustomersController.ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken),
                "list the shopper's Maxio subscriptions",
                MaxioErrorTranslator.IsNotFound).ConfigureAwait(false);
        }
        catch (ApiException exception) when (MaxioErrorTranslator.IsNotFound(exception))
        {
            return new List<SubscriptionResponse>();
        }
    }

    /// <summary>
    /// Creates the subscription under a deterministic reference. Maxio rejects a duplicate reference with a
    /// 422, and that rejection is the integration's server-side idempotency guarantee: a replayed request
    /// reads the original subscription back instead of creating a second one.
    /// </summary>
    private async Task<SubscribeResult> CreateSubscriptionAsync(
        AdvancedBillingClient client,
        Subscriber subscriber,
        Customer customer,
        SubscriptionPlan plan,
        MaxioSite site,
        CollectionMethod collectionMethod,
        string idempotencyKey,
        bool explicitIdempotencyKey,
        CancellationToken cancellationToken)
    {
        var prefix = _clientFactory.Options.ReferencePrefix;

        for (var attempt = 1; attempt <= MaxReferenceAttempts; attempt++)
        {
            var reference = MaxioReference.ForSubscription(prefix, subscriber.UserKey, idempotencyKey, attempt);

            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    Reference = reference,
                    PaymentCollectionMethod = collectionMethod,
                },
            };

            try
            {
                var response = await ExecuteAsync(
                    () => client.SubscriptionsController.CreateSubscriptionAsync(request, cancellationToken),
                    $"subscribe to plan '{plan.Handle}'",
                    MaxioErrorTranslator.IsReferenceConflict).ConfigureAwait(false);

                var subscription = MaxioMapper.ToSubscription(response.Subscription, site.Currency);

                _logger.LogInformation(
                    "Subscribed {UserKey} to plan {PlanHandle} as Maxio subscription {SubscriptionId} ({State}); next billing {NextBillingAt:O}.",
                    subscriber.UserKey, plan.Handle, subscription.Id, subscription.State, subscription.NextBillingAt);

                return new SubscribeResult(subscription, AlreadySubscribed: false, CustomerCreated: false);
            }
            catch (ApiException exception) when (MaxioErrorTranslator.IsReferenceConflict(exception))
            {
                var conflicting = await FindSubscriptionByReferenceAsync(client, reference, site, cancellationToken)
                    .ConfigureAwait(false);

                if (conflicting is null)
                {
                    // Maxio says the reference is taken but will not show us by what. There is nothing safe
                    // to retry here: inventing a fresh reference risks creating a duplicate subscription.
                    throw MaxioErrorTranslator.Translate(exception, $"subscribe to plan '{plan.Handle}'");
                }

                // A caller replaying an explicit idempotency key must always get the original result back,
                // whatever state that subscription is in now.
                if (explicitIdempotencyKey || conflicting.IsLive)
                {
                    _logger.LogInformation(
                        "Subscribe for {UserKey} on plan {PlanHandle} replayed reference {Reference}; returning existing subscription {SubscriptionId}.",
                        subscriber.UserKey, plan.Handle, reference, conflicting.Id);

                    return new SubscribeResult(conflicting, AlreadySubscribed: true, CustomerCreated: false);
                }

                // The natural reference belongs to a subscription that has ended, so this is a genuine
                // re-subscribe. Move to the next reference in the series and try again.
                _logger.LogDebug(
                    "Reference {Reference} is held by ended subscription {SubscriptionId} ({State}); retrying with the next reference.",
                    reference, conflicting.Id, conflicting.State);
            }
        }

        throw new SubscriptionBillingRejectedException(
            $"Could not find a free subscription reference for subscriber '{subscriber.UserKey}' on plan " +
            $"'{plan.Handle}' after {MaxReferenceAttempts} attempts.");
    }

    private async Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(
        AdvancedBillingClient client,
        string reference,
        MaxioSite site,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteAsync(
                () => client.SubscriptionsController.FindSubscriptionAsync(reference, cancellationToken),
                "look up a subscription by reference",
                MaxioErrorTranslator.IsNotFound).ConfigureAwait(false);

            return response.Subscription is null ? null : MaxioMapper.ToSubscription(response.Subscription, site.Currency);
        }
        catch (ApiException exception) when (MaxioErrorTranslator.IsNotFound(exception))
        {
            return null;
        }
    }

    /// <summary>
    /// Picks how Maxio collects payment for a new subscription. Configuration wins; otherwise the site's
    /// billing architecture decides. Either way the shopper is billed by invoice rather than being asked
    /// for a card at signup.
    /// </summary>
    private static CollectionMethod ResolveCollectionMethod(MaxioOptions options, MaxioSite site)
    {
        if (string.IsNullOrWhiteSpace(options.PaymentCollectionMethod))
        {
            return site.InvoicedCollectionMethod;
        }

        return MaxioEnum.FromWireName<CollectionMethod>(options.PaymentCollectionMethod)
            ?? throw new SubscriptionBillingNotConfiguredException(
                $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.PaymentCollectionMethod)} is " +
                $"'{options.PaymentCollectionMethod}', which Maxio does not recognise. Valid values are: " +
                string.Join(", ", MaxioEnum.WireNamesOf<CollectionMethod>()) + ".");
    }

    private string CustomerReferenceFor(Subscriber subscriber) =>
        MaxioReference.ForCustomer(_clientFactory.Options.ReferencePrefix, subscriber.UserKey);

    /// <summary>
    /// ASP.NET Identity only guarantees a user name and an email address, so the Maxio customer's name is
    /// best-effort: a supplied name is used as-is, otherwise the email local part is split on its separator.
    /// </summary>
    private static (string FirstName, string LastName) SplitName(Subscriber subscriber)
    {
        var localPart = LocalPart(subscriber.Email);

        if (!string.IsNullOrWhiteSpace(subscriber.FirstName) || !string.IsNullOrWhiteSpace(subscriber.LastName))
        {
            return (Or(subscriber.FirstName, localPart), Or(subscriber.LastName, "-"));
        }

        var separatorIndex = localPart.IndexOfAny(new[] { '.', '_', '-', '+' });

        return separatorIndex > 0 && separatorIndex < localPart.Length - 1
            ? (localPart.Substring(0, separatorIndex), localPart.Substring(separatorIndex + 1))
            : (localPart, "-");

        static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value!;
    }

    private static string LocalPart(string email)
    {
        var atIndex = email.IndexOf('@');
        return atIndex > 0 ? email.Substring(0, atIndex) : email;
    }

    /// <summary>
    /// Runs a call against Maxio and normalises its failures. Transport faults become a retryable outage,
    /// SDK <see cref="ApiException"/>s are translated into the application's billing exceptions, and the
    /// specific responses a caller wants to inspect (a 404 lookup miss, a reference conflict) are let
    /// through untouched via <paramref name="passThrough"/>.
    /// </summary>
    private static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> call,
        string operation,
        Func<ApiException, bool>? passThrough = null)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (ApiException exception) when (passThrough is not null && passThrough(exception))
        {
            throw;
        }
        catch (ApiException exception)
        {
            throw MaxioErrorTranslator.Translate(exception, operation);
        }
        catch (HttpRequestException exception)
        {
            throw new SubscriptionBillingUnavailableException($"Could not reach Maxio to {operation}.", exception);
        }
        catch (TaskCanceledException exception) when (exception.InnerException is TimeoutException)
        {
            throw new SubscriptionBillingUnavailableException($"The call to Maxio to {operation} timed out.", exception);
        }
    }
}
