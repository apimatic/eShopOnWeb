using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing.
/// </summary>
/// <remarks>
/// Maxio is the system of record: nothing about customers or subscriptions is mirrored locally.
/// The link between the two systems is the customer reference derived from the authenticated
/// user, which is what lets a signup be repeated safely without a local mapping table.
/// </remarks>
internal class MaxioSubscriptionService : ISubscriptionService
{
    private const string PlanCacheKey = "Maxio:Plans";

    /// <summary>
    /// eShopOnWeb does not collect card details as part of subscribing, so subscriptions are
    /// invoiced rather than auto-charged. Left to the site default, Maxio would try to capture the
    /// first period immediately and reject the signup with "No payment method was on file".
    /// Collecting a card would mean a Billing.js token exchange, which this flow deliberately avoids.
    /// </summary>
    private const string PaymentCollectionMethod = "remittance";

    private static readonly TimeSpan PlanCacheDuration = TimeSpan.FromSeconds(60);

    private readonly MaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _signupLock;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioApiClient client, IOptions<MaxioSettings> settings,
        IMemoryCache cache, KeyedAsyncLock signupLock, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = MaxioOptionsAccessor.Resolve(settings);
        _cache = cache;
        _signupLock = signupLock;
        _logger = logger;
    }

    private string ProductFamilyHandle => _settings.ProductFamilyHandle!;

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(PlanCacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var products = await _client.ListProductsForFamilyAsync(ProductFamilyHandle, cancellationToken);

        var plans = products
            // Archived products cannot be subscribed to, and a product without a handle cannot be
            // addressed by one - neither belongs in a list of things a shopper can buy.
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(MaxioMappings.ToPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _cache.Set(PlanCacheKey, (IReadOnlyList<SubscriptionPlan>)plans, PlanCacheDuration);
        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = await FindPlanAsync(request.PlanHandle, cancellationToken)
                   ?? throw new PlanNotFoundException(request.PlanHandle, ProductFamilyHandle);

        var customerReference = MaxioReferences.ForCustomer(request.Customer.UserIdentifier);

        // Serialise signups for this user so a double submit cannot race past the existing
        // subscription check below.
        using var _ = await _signupLock.AcquireAsync(customerReference, cancellationToken);

        var (customer, customerCreated) = await EnsureCustomerAsync(request.Customer, customerReference, cancellationToken);

        var existing = await FindSubscriptionOnPlanAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Maxio customer {CustomerId} is already subscribed to {PlanHandle} (subscription {SubscriptionId}, state {State}); returning the existing subscription.",
                customer.Id, plan.Handle, existing.Id, existing.State);
            return SubscribeResult.AlreadySubscribed(existing);
        }

        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioSubscriptionAttributes
            {
                ProductHandle = plan.Handle,
                ProductPricePointHandle = request.PricePointHandle,
                CustomerId = customer.Id,
                Reference = MaxioReferences.ForSubscription(request.Customer.UserIdentifier, plan.Handle),
                PaymentCollectionMethod = PaymentCollectionMethod
            },
            // A caller-supplied key extends duplicate protection across instances; without one a
            // per-call token still protects against this integration replaying its own request.
            UniquenessToken = request.IdempotencyKey ?? Guid.NewGuid().ToString("N")
        };

        try
        {
            var created = await _client.CreateSubscriptionAsync(body, cancellationToken);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} on {PlanHandle} for customer {CustomerId} (state {State}).",
                created.Id, plan.Handle, customer.Id, created.State);
            return SubscribeResult.NewlyCreated(MaxioMappings.ToSubscription(created), customerCreated);
        }
        catch (MaxioDuplicateSubmissionException ex)
        {
            // Maxio saw this exact submission before. It will not say whether the first one
            // succeeded, so re-read the customer's subscriptions and answer from provider state.
            _logger.LogWarning(ex,
                "Maxio reported a duplicate submission for customer {CustomerId} on {PlanHandle}; re-reading provider state.",
                customer.Id, plan.Handle);

            var resolved = await FindSubscriptionOnPlanAsync(customer.Id, plan.Handle, cancellationToken);
            if (resolved is not null)
            {
                return SubscribeResult.AlreadySubscribed(resolved);
            }

            throw new BillingProviderException(
                "Maxio rejected the signup as a duplicate submission but no matching subscription exists yet. " +
                "Retry in a few moments; the original request may still be settling.",
                statusCode: 409, errors: ex.Errors, innerException: ex);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string userIdentifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdentifier);

        var customerReference = MaxioReferences.ForCustomer(userIdentifier);
        var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            // The user has never subscribed, so there is no customer to create yet.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .Select(MaxioMappings.ToSubscription)
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(subscription => subscription.Id)
            .ToList();
    }

    /// <summary>
    /// Resolves a plan handle against the configured product family. Restricting signups to that
    /// family means a caller cannot subscribe to an unrelated product that happens to share the site.
    /// </summary>
    private async Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(plan => string.Equals(plan.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<(MaxioCustomer Customer, bool Created)> EnsureCustomerAsync(BillingCustomerProfile profile,
        string customerReference, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return (existing, false);
        }

        var attributes = new MaxioCustomerAttributes
        {
            Email = profile.Email,
            FirstName = FirstNameFor(profile),
            LastName = LastNameFor(profile),
            Reference = customerReference
        };

        try
        {
            var created = await _client.CreateCustomerAsync(attributes, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.",
                created.Id, customerReference);
            return (created, true);
        }
        catch (MaxioUnprocessableEntityException ex)
        {
            // Most likely another caller created the customer between the lookup and the create:
            // Maxio enforces uniqueness on the reference. Re-read before treating it as an error.
            var raced = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation(
                    "Maxio customer {CustomerId} for reference {CustomerReference} was created concurrently; using it.",
                    raced.Id, customerReference);
                return (raced, false);
            }

            _logger.LogError(ex, "Maxio rejected the customer details for reference {CustomerReference}.",
                customerReference);
            throw;
        }
    }

    private async Task<CustomerSubscription?> FindSubscriptionOnPlanAsync(long customerId, string planHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions
            .Select(MaxioMappings.ToSubscription)
            .Where(subscription =>
                string.Equals(subscription.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                SubscriptionStates.OccupiesPlanSlot(subscription.State))
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(subscription => subscription.Id)
            .FirstOrDefault();
    }

    // Maxio requires a first and last name on a customer, but eShopOnWeb only knows an email
    // address, so derive something usable rather than failing the signup.
    private static string FirstNameFor(BillingCustomerProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.FirstName)
            ? profile.FirstName!.Trim()
            : profile.Email.Split('@')[0];

    private static string LastNameFor(BillingCustomerProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.LastName)
            ? profile.LastName!.Trim()
            : "eShopOnWeb";
}
