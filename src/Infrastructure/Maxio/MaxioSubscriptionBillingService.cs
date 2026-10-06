using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing capability exposed to the eShopOnWeb application layer,
/// backed by Maxio Advanced Billing as the billing system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans offered by eShopOnWeb (the products of the
    /// configured Maxio product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Guarantees a Maxio customer exists for the given eShopOnWeb user.
    /// Idempotent: the Maxio customer reference is derived from the user id, so
    /// repeated calls (or a double-click) never create a second customer.
    /// </summary>
    Task<MaxioCustomerInfo> EnsureCustomerAsync(SubscriberInfo subscriber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the user to a plan. Idempotent: if the user already holds a
    /// live subscription for the plan, that subscription is returned instead of
    /// creating a duplicate.
    /// When <paramref name="planHandle"/> is null, the configured default plan
    /// (Maxio:DefaultPlanHandle) is used.
    /// </summary>
    Task<SubscriptionDetails> SubscribeAsync(SubscriberInfo subscriber, string? planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all Maxio subscriptions belonging to the user. Returns an empty
    /// list when the user has no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> GetUserSubscriptionsAsync(SubscriberInfo subscriber, CancellationToken cancellationToken = default);
}

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// Subscription states that represent a live (non-end-of-life) subscription.
    /// A live subscription for the same plan makes a subscribe call a no-op.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "soft_failure", "past_due", "unpaid"
    };

    private readonly IMaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(_options.ProductFamilyHandle!, cancellationToken);

        return products
            .Where(p => string.IsNullOrEmpty(p.ArchivedAt))
            .Select(ToPlan)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<MaxioCustomerInfo> EnsureCustomerAsync(SubscriberInfo subscriber, CancellationToken cancellationToken = default)
    {
        var reference = CustomerReferenceFor(subscriber.UserId);

        var existing = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return new MaxioCustomerInfo(existing.Id, existing.Reference ?? reference, existing.Email);
        }

        try
        {
            var created = await _client.CreateCustomerAsync(
                reference,
                subscriber.FirstName ?? "eShop",
                subscriber.LastName ?? "Customer",
                subscriber.Email,
                cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} (reference {Reference}) for user {UserId}.",
                created.Id, reference, subscriber.UserId);

            return new MaxioCustomerInfo(created.Id, created.Reference ?? reference, created.Email);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request may have created the customer for this
            // reference in between the lookup and the create (Maxio enforces
            // unique references). Re-look-up instead of failing the signup.
            _logger.LogWarning("Maxio customer creation for reference {Reference} was rejected (likely created concurrently); re-looking up.", reference);

            var raced = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return new MaxioCustomerInfo(raced.Id, raced.Reference ?? reference, raced.Email);
            }

            throw;
        }
    }

    public async Task<SubscriptionDetails> SubscribeAsync(SubscriberInfo subscriber, string? planHandle, CancellationToken cancellationToken = default)
    {
        var handle = string.IsNullOrWhiteSpace(planHandle) ? _options.DefaultPlanHandle : planHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new MaxioPlanNotFoundException(planHandle ?? string.Empty, _options.ProductFamilyHandle!);
        }

        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new MaxioPlanNotFoundException(handle, _options.ProductFamilyHandle!);
        }

        // Ensure the Maxio customer exists first — idempotent by reference.
        var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

        // Second idempotency guard: a live subscription to the same plan is
        // returned as-is so a double-click never creates two subscriptions.
        var existingSubscriptions = await _client.ListCustomerSubscriptionsAsync(customer.MaxioCustomerId, cancellationToken);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
            s.State is not null && LiveStates.Contains(s.State));

        if (existing is not null)
        {
            _logger.LogInformation("User {UserId} is already subscribed to plan {PlanHandle} (subscription {SubscriptionId}).",
                subscriber.UserId, plan.Handle, existing.Id);
            return ToDetails(existing, alreadySubscribed: true);
        }

        // Plans that do not require a payment method are billed by remittance
        // (invoice at renewal) so signup works without card capture / 3-DS.
        var paymentCollectionMethod = plan.RequiresPaymentMethod ? "automatic" : "remittance";
        var created = await _client.CreateSubscriptionAsync(plan.Handle, customer.Reference, paymentCollectionMethod, cancellationToken);

        _logger.LogInformation("User {UserId} subscribed to plan {PlanHandle}; Maxio subscription {SubscriptionId} in state {State}.",
            subscriber.UserId, plan.Handle, created.Id, created.State);

        return ToDetails(created, alreadySubscribed: false);
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetUserSubscriptionsAsync(SubscriberInfo subscriber, CancellationToken cancellationToken = default)
    {
        var reference = CustomerReferenceFor(subscriber.UserId);
        var customer = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .Select(s => ToDetails(s, alreadySubscribed: false))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>
    /// Stable, namespaced mapping from the eShopOnWeb user id to the unique
    /// Maxio customer reference.
    /// </summary>
    private static string CustomerReferenceFor(string userId) => $"eshoponweb-user-{userId}";

    private static SubscriptionPlan ToPlan(MaxioApiProduct product) => new(
        product.Handle ?? product.Id.ToString(),
        product.Name ?? product.Handle ?? product.Id.ToString(),
        product.Description,
        product.PriceInCents,
        ToPrice(product.PriceInCents),
        product.Interval,
        product.IntervalUnit,
        product.RequireCreditCard);

    private static SubscriptionDetails ToDetails(MaxioApiSubscription subscription, bool alreadySubscribed)
    {
        var priceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0;

        return new SubscriptionDetails(
            subscription.Id,
            subscription.State ?? string.Empty,
            subscription.Product?.Handle,
            subscription.Product?.Name,
            priceInCents,
            ToPrice(priceInCents),
            MaxioTimestamp.TryParse(subscription.CurrentPeriodEndsAt) ?? MaxioTimestamp.TryParse(subscription.NextAssessmentAt),
            MaxioTimestamp.TryParse(subscription.ActivatedAt),
            MaxioTimestamp.TryParse(subscription.CreatedAt),
            subscription.CancelAtEndOfPeriod ?? false,
            alreadySubscribed);
    }

    private static decimal ToPrice(long priceInCents) => priceInCents / 100m;
}