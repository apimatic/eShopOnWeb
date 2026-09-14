using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string PlanCacheKeyPrefix = "maxio:plans:";

    private readonly IMaxioApiClient _client;
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _subscriberLock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IOptionsMonitor<MaxioSettings> settings,
        IMemoryCache cache,
        KeyedAsyncLock subscriberLock,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings;
        _cache = cache;
        _subscriberLock = subscriberLock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var settings = EnsureConfigured();
        var familyHandle = settings.ProductFamilyHandle!.Trim();
        var cacheKey = PlanCacheKeyPrefix + familyHandle;

        if (settings.PlanCacheDuration > TimeSpan.Zero &&
            _cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlan>? cached) &&
            cached is not null)
        {
            return cached;
        }

        var products = await _client.ListProductsForProductFamilyAsync(familyHandle, cancellationToken)
            .ConfigureAwait(false);

        var plans = products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(MaxioMapper.ToPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (settings.PlanCacheDuration > TimeSpan.Zero)
        {
            _cache.Set(cacheKey, (IReadOnlyList<SubscriptionPlan>)plans, settings.PlanCacheDuration);
        }

        return plans;
    }

    public async Task<SubscriptionPlan?> GetPlanAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        var plans = await GetPlansAsync(cancellationToken).ConfigureAwait(false);
        return plans.FirstOrDefault(plan => string.Equals(plan.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscribeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var settings = EnsureConfigured();

        var plan = await GetPlanAsync(command.PlanHandle, cancellationToken).ConfigureAwait(false)
            ?? throw new SubscriptionPlanNotFoundException(command.PlanHandle);

        var customerReference = MaxioReference.ForCustomer(
            settings.CustomerReferencePrefix,
            command.Subscriber.ExternalId);

        // Serialise concurrent subscribe attempts for this shopper so the "does one already exist"
        // check below cannot race with itself - the double-click case.
        using (await _subscriberLock.AcquireAsync(customerReference, cancellationToken).ConfigureAwait(false))
        {
            // A caller-supplied idempotency key is recorded as the subscription reference, so a
            // retried request finds the subscription the first attempt created even after a restart.
            var subscriptionReference = string.IsNullOrWhiteSpace(command.IdempotencyKey)
                ? null
                : command.IdempotencyKey.Trim();

            if (subscriptionReference is not null)
            {
                var replay = await _client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken)
                    .ConfigureAwait(false);

                if (replay is not null)
                {
                    _logger.LogInformation(
                        "Subscribe replayed for customer reference {CustomerReference}: idempotency key already produced subscription {SubscriptionId}.",
                        customerReference,
                        replay.Id);

                    return new SubscribeResult(MaxioMapper.ToSubscription(replay), Created: false, plan);
                }
            }

            var customer = await EnsureCustomerAsync(command.Subscriber, customerReference, cancellationToken)
                .ConfigureAwait(false);

            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                _logger.LogInformation(
                    "Customer {CustomerId} already holds subscription {SubscriptionId} to plan '{PlanHandle}' in state '{State}'; returning it instead of subscribing again.",
                    customer.Id,
                    existing.Id,
                    plan.Handle,
                    existing.State);

                return new SubscribeResult(MaxioMapper.ToSubscription(existing), Created: false, plan);
            }

            MaxioSubscription created;
            try
            {
                created = await _client.CreateSubscriptionAsync(
                    new MaxioCreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customer.Id,
                        Reference = subscriptionReference,
                        PaymentCollectionMethod = settings.EffectivePaymentCollectionMethod
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // Creation was rejected. If something did get created concurrently - by another host,
                // outside this process lock - the shopper already has what they asked for.
                var concurrent = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken)
                    .ConfigureAwait(false);

                if (concurrent is not null)
                {
                    return new SubscribeResult(MaxioMapper.ToSubscription(concurrent), Created: false, plan);
                }

                throw new SubscriptionBillingValidationException(ex.Errors, ex);
            }

            _logger.LogInformation(
                "Created subscription {SubscriptionId} to plan '{PlanHandle}' for customer {CustomerId} ({CustomerReference}); state '{State}', next billing {NextBillingAt:o}.",
                created.Id,
                plan.Handle,
                customer.Id,
                customerReference,
                created.State,
                created.NextAssessmentAt ?? created.CurrentPeriodEndsAt);

            return new SubscribeResult(MaxioMapper.ToSubscription(created), Created: true, plan);
        }
    }

    public async Task<SubscriberSubscriptions> GetSubscriptionsAsync(
        Subscriber subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var settings = EnsureConfigured();
        var customerReference = MaxioReference.ForCustomer(settings.CustomerReferencePrefix, subscriber.ExternalId);

        var customer = await _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken)
            .ConfigureAwait(false);

        if (customer is null)
        {
            // Reading someone's subscriptions must not create a billing record for them.
            return new SubscriberSubscriptions(customerReference, CustomerId: null, Array.Empty<CustomerSubscription>());
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken)
            .ConfigureAwait(false);

        var mapped = subscriptions
            .Select(MaxioMapper.ToSubscription)
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(subscription => subscription.Id)
            .ToList();

        return new SubscriberSubscriptions(customerReference, customer.Id, mapped);
    }

    /// <summary>
    /// Returns the Maxio customer for this shopper, creating it if it does not exist yet. Safe to
    /// call concurrently: the reference is unique per site, so a lost race surfaces as a 422 and is
    /// resolved by reading the customer the winner created.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(
        Subscriber subscriber,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    FirstName = subscriber.FirstName,
                    LastName = subscriber.LastName,
                    Email = subscriber.Email,
                    Organization = subscriber.Organization,
                    Reference = customerReference
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for reference {CustomerReference}.",
                created.Id,
                customerReference);

            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken)
                .ConfigureAwait(false);

            if (raced is not null)
            {
                _logger.LogInformation(
                    "Customer creation for reference {CustomerReference} lost a race; using customer {CustomerId}.",
                    customerReference,
                    raced.Id);

                return raced;
            }

            throw new SubscriptionBillingValidationException(ex.Errors, ex);
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken)
            .ConfigureAwait(false);

        return subscriptions
            .Where(subscription => SubscriptionStates.IsLive(subscription.State))
            .Where(subscription => string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    private MaxioSettings EnsureConfigured()
    {
        var settings = _settings.CurrentValue;
        var problems = settings.Validate();

        if (problems.Count > 0)
        {
            throw new SubscriptionBillingConfigurationException(problems);
        }

        return settings;
    }
}
