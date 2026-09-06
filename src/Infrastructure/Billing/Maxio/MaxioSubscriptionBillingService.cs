using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing as the system of record.
/// </summary>
/// <remarks>
/// No subscription state is stored locally. Every read goes to Maxio, and the link between an
/// eShopOnWeb user and their Maxio customer is carried by a deterministic <c>reference</c>
/// (see <see cref="MaxioReferenceFactory"/>) rather than a local table, which is what makes the
/// enrollment idempotent and lets the mapping outlive this process.
/// </remarks>
internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private readonly IMaxioApiClient _client;
    private readonly MaxioSettingsProvider _settingsProvider;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        MaxioSettingsProvider settingsProvider,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settingsProvider = settingsProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsProvider.GetValidated();

        try
        {
            var products = await _client
                .ListProductsForProductFamilyAsync(settings.ProductFamilyHandle!, cancellationToken)
                .ConfigureAwait(false);

            return products
                .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
                .Select(ToPlan)
                .OrderBy(plan => plan.PriceInCents)
                .ThenBy(plan => plan.Handle, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, $"listing plans in product family '{settings.ProductFamilyHandle}'");
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = _settingsProvider.GetValidated();

        // Only plans published by the configured product family may be subscribed to, so an arbitrary
        // handle from the request body cannot reach a product this storefront does not offer.
        var plan = (await GetPlansAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new PlanNotFoundException(request.PlanHandle, settings.ProductFamilyHandle);

        var subscriptionReference = MaxioReferenceFactory.ForSubscription(request.Subscriber, plan.Handle, request.IdempotencyKey);

        var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return ExistingEnrollment(existing, plan.Handle, subscriptionReference);
        }

        var customer = await EnsureCustomerAsync(request.Subscriber, cancellationToken).ConfigureAwait(false);

        try
        {
            var created = await _client.CreateSubscriptionAsync(
                new MaxioSubscriptionAttributes
                {
                    ProductHandle = plan.Handle,
                    CustomerReference = customer.Reference,
                    Reference = subscriptionReference,
                    PaymentCollectionMethod = settings.PaymentCollectionMethod
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Subscribed customer {CustomerId} to plan {PlanHandle}; Maxio subscription {SubscriptionId} is {State}.",
                customer.Id, plan.Handle, created.Id, created.State);

            return new SubscribeResult(ToSubscription(created), Created: true);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // The reference is unique in Maxio, so a rejected create may simply mean a concurrent
            // request (or a retried one) already created this exact enrollment. Re-reading by
            // reference distinguishes that from a genuine rejection without matching on error text.
            var raced = await FindSubscriptionAsync(subscriptionReference, cancellationToken).ConfigureAwait(false);
            if (raced is not null)
            {
                _logger.LogInformation(
                    "Concurrent subscribe for reference {Reference} resolved to existing Maxio subscription {SubscriptionId}.",
                    subscriptionReference, raced.Id);

                return ExistingEnrollment(raced, plan.Handle, subscriptionReference);
            }

            throw new BillingValidationException(ex.Errors.Count > 0 ? ex.Errors : new[] { ex.Message });
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, $"subscribing to plan '{plan.Handle}'");
        }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        _settingsProvider.GetValidated();

        var customerReference = MaxioReferenceFactory.ForCustomer(subscriber);

        try
        {
            var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken).ConfigureAwait(false);
            if (customer is null)
            {
                // Never enrolled: an empty list, not an error.
                return Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);

            return subscriptions
                .Select(ToSubscription)
                .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
                .ThenByDescending(subscription => subscription.Id)
                .ToList();
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "listing subscriptions");
        }
    }

    /// <summary>
    /// Resolves the Maxio customer that mirrors the eShopOnWeb user, creating it on first use.
    /// Safe to run concurrently: the loser of a create race is rescued by a second lookup.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var reference = MaxioReferenceFactory.ForCustomer(subscriber);

        try
        {
            var existing = await _client.FindCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            var (firstName, lastName) = ResolveName(subscriber);

            var created = await _client.CreateCustomerAsync(
                new MaxioCustomerAttributes
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = subscriber.Email,
                    Reference = reference
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", created.Id, reference);

            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _client.FindCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
            if (raced is not null)
            {
                return raced;
            }

            throw new BillingValidationException(ex.Errors.Count > 0 ? ex.Errors : new[] { ex.Message });
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "resolving the billing customer");
        }
    }

    private async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.FindSubscriptionByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "looking up an existing subscription");
        }
    }

    private SubscribeResult ExistingEnrollment(MaxioSubscription subscription, string planHandle, string reference)
    {
        var mapped = ToSubscription(subscription);

        if (SubscriptionStates.IsTerminal(mapped.State))
        {
            throw new SubscriptionConflictException(mapped.Id, mapped.State, planHandle);
        }

        _logger.LogInformation(
            "Subscribe request for reference {Reference} matched existing Maxio subscription {SubscriptionId} in state {State}; nothing created.",
            reference, mapped.Id, mapped.State);

        return new SubscribeResult(mapped, Created: false);
    }

    /// <summary>
    /// Maxio requires a name on a customer. eShopOnWeb only knows a user name, so the local part of
    /// the email stands in for the first name and the site name for the last, unless the caller's
    /// identity carries something better.
    /// </summary>
    private static (string FirstName, string LastName) ResolveName(SubscriberIdentity subscriber)
    {
        if (!string.IsNullOrWhiteSpace(subscriber.FirstName) || !string.IsNullOrWhiteSpace(subscriber.LastName))
        {
            return (subscriber.FirstName ?? string.Empty, subscriber.LastName ?? string.Empty);
        }

        var at = subscriber.Email.IndexOf('@');
        var localPart = at > 0 ? subscriber.Email[..at] : subscriber.Email;

        return (localPart, "eShopOnWeb");
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new(
        Handle: product.Handle!,
        Name: product.Name ?? product.Handle!,
        Description: product.Description,
        PriceInCents: product.PriceInCents,
        Interval: product.Interval,
        IntervalUnit: product.IntervalUnit ?? string.Empty,
        ProductFamilyHandle: product.ProductFamily?.Handle,
        RequiresPaymentMethod: product.RequireCreditCard,
        Taxable: product.Taxable,
        TrialPriceInCents: product.TrialPriceInCents,
        TrialInterval: product.TrialInterval,
        TrialIntervalUnit: product.TrialIntervalUnit);

    private static CustomerSubscription ToSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State
                ?? throw new BillingProviderException($"Maxio returned subscription {subscription.Id} without a state."),
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CustomerId = subscription.Customer?.Id ?? 0,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        TrialEndedAt = subscription.TrialEndedAt,
        CanceledAt = subscription.CanceledAt,
        ExpiresAt = subscription.ExpiresAt,
        CreatedAt = subscription.CreatedAt
    };

    /// <summary>Turns a provider-level failure into the domain failure it means.</summary>
    private BillingException Translate(MaxioApiException exception, string operation)
    {
        _logger.LogError(exception,
            "Maxio call failed while {Operation} (status {StatusCode}, request id {RequestId}).",
            operation, (int)exception.StatusCode, exception.RequestId ?? "n/a");

        return exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new BillingConfigurationException(
                $"Maxio rejected the configured API key while {operation}. Check '{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)}' " +
                $"and '{MaxioSettings.SectionName}:{nameof(MaxioSettings.Subdomain)}'.", exception),

            HttpStatusCode.NotFound => new BillingProviderException(
                $"Maxio has no such resource while {operation}. Check '{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}'.",
                exception, exception.StatusCode, exception.RequestId),

            HttpStatusCode.UnprocessableEntity => new BillingValidationException(
                exception.Errors.Count > 0 ? exception.Errors : new[] { exception.Message }),

            _ => new BillingProviderException(
                $"Maxio failed while {operation}.", exception, exception.StatusCode, exception.RequestId)
        };
    }
}
