using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Controllers;
using AdvancedBilling.Standard.Exceptions;
using AdvancedBilling.Standard.Models;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing (formerly Chargify)
/// via the official Maxio .NET SDK. Maxio is the system of record: idempotency is anchored on a
/// stable customer <c>reference</c> derived from the user's email and on the subscriptions Maxio
/// already holds, rather than on any local persistence.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // A subscription in one of these Maxio states is over; re-subscribing is allowed.
    // Any other state (active, trialing, past_due, on_hold, ...) counts as a live subscription.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "trial_ended", "failed_to_create"
    };

    private readonly AdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly KeyedAsyncLock _subscribeLock;

    public MaxioBillingService(
        AdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingService> logger,
        KeyedAsyncLock subscribeLock)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
        _subscribeLock = subscribeLock;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(
        CancellationToken cancellationToken = default)
    {
        var input = new ListProductsForProductFamilyInput
        {
            // "handle:" lets Maxio resolve the family by its stable handle instead of a numeric id.
            ProductFamilyId = $"handle:{_options.ProductFamilyHandle}",
            PerPage = 200,
            IncludeArchived = false
        };

        List<ProductResponse> products;
        try
        {
            products = await _client.ProductFamiliesController
                .ListProductsForProductFamilyAsync(input, cancellationToken);
        }
        catch (ApiException ex)
        {
            throw Translate(ex, $"listing plans for product family '{_options.ProductFamilyHandle}'");
        }

        return products
            .Select(p => p.Product)
            .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException("A plan handle is required to subscribe.");
        }

        // Validate the requested plan against the catalog so we fail fast with a helpful message
        // instead of a raw Maxio 422.
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            var available = string.Join(", ", plans.Select(p => p.Handle));
            throw new BillingException(
                $"Unknown plan '{planHandle}'. Available plans: {(available.Length > 0 ? available : "(none)")}.");
        }

        // Serialize concurrent subscribe calls for the same shopper (e.g. a double-click) so the
        // "ensure customer + check existing + create" sequence is not interleaved and cannot
        // produce duplicate customers or subscriptions within this instance.
        using (await _subscribeLock.AcquireAsync(BuildReference(subscriber.Email), cancellationToken))
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);
            var customerId = customer.Id!.Value;

            // Idempotency: if the shopper already has a live subscription to this plan (e.g. a
            // double-click, or an earlier successful call), return it rather than creating a second.
            var existing = await GetCustomerSubscriptionsAsync(customerId, cancellationToken);
            var live = existing.FirstOrDefault(s =>
                string.Equals(s.Subscription.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                !TerminalStates.Contains(EnumWire(s.Subscription.State)));
            if (live is not null)
            {
                _logger.LogInformation(
                    "Shopper already has live subscription {SubscriptionId} to plan {PlanHandle}; returning existing.",
                    live.Subscription.Id, plan.Handle);
                return new SubscribeResult(MapSubscription(live.Subscription), alreadyExisted: true, customerId);
            }

            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customerId,
                    // The seeded plans do not require a stored payment method; remittance (invoice)
                    // collection lets a shopper enroll without card capture / 3-DS.
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            };

            try
            {
                var created = await _client.SubscriptionsController
                    .CreateSubscriptionAsync(request, cancellationToken);
                _logger.LogInformation(
                    "Created subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
                    created.Subscription.Id, customerId, plan.Handle);
                return new SubscribeResult(MapSubscription(created.Subscription), alreadyExisted: false, customerId);
            }
            catch (ErrorListResponseException ex)
            {
                // Maxio rejected the request (e.g. payment method required). Surface its own messages.
                var detail = ex.Errors is { Count: > 0 } ? string.Join("; ", ex.Errors) : ex.Message;
                throw new BillingException($"Maxio could not create the subscription: {detail}", ex);
            }
            catch (ApiException ex)
            {
                throw Translate(ex, $"creating a subscription to plan '{plan.Handle}'");
            }
        }
    }

    public async Task<IReadOnlyCollection<SubscriptionSummary>> ListSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerAsync(subscriber, cancellationToken);
        if (customer is null)
        {
            // No Maxio customer yet means the shopper has never subscribed.
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await GetCustomerSubscriptionsAsync(customer.Id!.Value, cancellationToken);
        return subscriptions
            .Select(s => MapSubscription(s.Subscription))
            .OrderByDescending(s => s.CurrentPeriodStartedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>
    /// Returns the Maxio customer for the user, creating one if none exists. Safe under concurrent
    /// calls: a create that loses a race (duplicate reference) falls back to re-reading.
    /// </summary>
    private async Task<Customer> EnsureCustomerAsync(
        SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(subscriber, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var reference = BuildReference(subscriber.Email);
        var (firstName, lastName) = DeriveName(subscriber.Email);
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                Email = subscriber.Email,
                Reference = reference,
                FirstName = firstName,
                LastName = lastName
            }
        };

        try
        {
            var created = await _client.CustomersController.CreateCustomerAsync(request, cancellationToken);
            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for reference {Reference}.",
                created.Customer.Id, reference);
            return created.Customer;
        }
        catch (ApiException ex) when (ex is ErrorListResponseException or CustomerErrorResponseException)
        {
            // Likely a concurrent create (reference must be unique). Re-read; if it is now present,
            // the other caller won the race and we return their customer.
            var afterRace = await FindCustomerAsync(subscriber, cancellationToken);
            if (afterRace is not null)
            {
                return afterRace;
            }

            throw Translate(ex, $"creating a customer for reference '{reference}'");
        }
    }

    /// <summary>Looks up the user's Maxio customer by reference; null when none exists (404).</summary>
    private async Task<Customer?> FindCustomerAsync(
        SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var reference = BuildReference(subscriber.Email);
        try
        {
            var response = await _client.CustomersController
                .ReadCustomerByReferenceAsync(reference, cancellationToken);
            return response.Customer;
        }
        catch (ApiException ex) when (ex.ResponseCode == 404)
        {
            return null;
        }
        catch (ApiException ex)
        {
            throw Translate(ex, $"looking up a customer for reference '{reference}'");
        }
    }

    private async Task<List<SubscriptionResponse>> GetCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.CustomersController
                .ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        }
        catch (ApiException ex)
        {
            throw Translate(ex, $"listing subscriptions for customer {customerId}");
        }
    }

    private SubscriptionPlan MapPlan(Product product) => new(
        handle: product.Handle,
        name: product.Name,
        description: product.Description,
        priceInCents: product.PriceInCents ?? 0,
        interval: product.Interval ?? 0,
        intervalUnit: EnumWire(product.IntervalUnit),
        requiresPaymentMethod: product.RequireCreditCard ?? false);

    private SubscriptionSummary MapSubscription(Subscription subscription)
    {
        var product = subscription.Product;
        return new SubscriptionSummary(
            id: subscription.Id ?? 0,
            state: EnumWire(subscription.State),
            planHandle: product?.Handle ?? string.Empty,
            planName: product?.Name ?? string.Empty,
            priceInCents: subscription.ProductPriceInCents ?? product?.PriceInCents ?? 0,
            interval: product?.Interval ?? 0,
            intervalUnit: EnumWire(product?.IntervalUnit),
            paymentCollectionMethod: EnumWire(subscription.PaymentCollectionMethod),
            currentPeriodStartedAt: subscription.CurrentPeriodStartedAt,
            nextBillingAt: subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }

    /// <summary>Namespaced, stable customer reference derived from the user's email.</summary>
    private static string BuildReference(string email) => $"eshoponweb:{email.Trim().ToLowerInvariant()}";

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var local = email.Split('@')[0];
        var first = string.IsNullOrWhiteSpace(local) ? "eShopOnWeb" : local;
        return (first, "(eShopOnWeb)");
    }

    private BillingProviderException Translate(ApiException ex, string operation)
    {
        _logger.LogError(ex, "Maxio API error while {Operation} (HTTP {StatusCode}).", operation, ex.ResponseCode);
        return new BillingProviderException($"The billing provider returned an error while {operation}.", ex);
    }

    /// <summary>
    /// Returns the wire string for an SDK enum value (honoring its [EnumMember] name, e.g.
    /// "trial_ended"), so the API reports exactly the state Maxio uses. Empty string for null.
    /// </summary>
    private static string EnumWire<TEnum>(TEnum? value) where TEnum : struct, Enum
    {
        if (value is null)
        {
            return string.Empty;
        }

        var name = value.Value.ToString();
        var member = typeof(TEnum).GetField(name)?.GetCustomAttribute<EnumMemberAttribute>();
        return member?.Value ?? name.ToLowerInvariant();
    }
}
