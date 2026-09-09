using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
///
/// Idempotency design (no local persistence — Maxio is the system of record):
/// - The eShopOnWeb user id is mapped to a Maxio customer via a deterministic
///   <c>reference</c> value. Customer lookup-by-reference runs first; creation is
///   attempted only on a miss, and a duplicate-reference rejection is resolved by
///   re-reading the already-created customer. Maxio enforces reference uniqueness,
///   so a double-click can never create two customers.
/// - Subscribe serializes per user, then checks the customer's existing live
///   subscriptions for the requested plan before creating; a match short-circuits
///   creation, so a double-click can never create two live subscriptions.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string CustomerReferencePrefix = "eshopweb-user:";

    /// <summary>
    /// Subscription states that represent a live/problem subscription the shopper
    /// effectively "holds". End-of-life states (canceled, expired, trial_ended,
    /// on_hold, failed_to_create) do not block a new subscribe.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "awaiting_signup", "assessing", "trialing", "active",
        "soft_failure", "past_due", "unpaid", "suspended"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserGates = new(StringComparer.Ordinal);

    private readonly MaxioApiClient _apiClient;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioApiClient apiClient,
        IOptions<MaxioOptions> options,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _apiClient = apiClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _apiClient.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle ?? p.Id.ToString(),
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                RequiresPaymentMethod = p.RequireCreditCard
            })
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string userId, string userName, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new PlanNotFoundException(planHandle ?? string.Empty);
        }

        var customerReference = BuildCustomerReference(userId);
        var gate = UserGates.GetOrAdd(customerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(customerReference, userName, email, cancellationToken);

            var plans = await ListPlansAsync(cancellationToken);
            var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                throw new PlanNotFoundException(planHandle);
            }

            var subscriptions = await _apiClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = subscriptions.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                LiveStates.Contains(s.State));

            if (existing is not null)
            {
                _logger.LogInformation(
                    "Subscribe short-circuited: user {UserId} already holds subscription {SubscriptionId} in state {State} for plan {PlanHandle}.",
                    userId, existing.Id, existing.State, planHandle);
                return new SubscribeResult(Map(existing), CreatedNew: false);
            }

            var created = await _apiClient.CreateSubscriptionAsync(planHandle, customer.Id, _options.PaymentCollectionMethod, cancellationToken);
            _logger.LogInformation(
                "Created subscription {SubscriptionId} (state {State}) for user {UserId} on plan {PlanHandle}.",
                created.Id, created.State, userId, planHandle);

            return new SubscribeResult(Map(created), CreatedNew: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListUserSubscriptionsAsync(string userId, string userName, string email, CancellationToken cancellationToken = default)
    {
        var customerReference = BuildCustomerReference(userId);
        var customer = await EnsureCustomerAsync(customerReference, userName, email, cancellationToken);
        var subscriptions = await _apiClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(Map)
            .ToList();
    }

    /// <summary>
    /// Idempotently resolves the Maxio customer for the eShopOnWeb user. The
    /// deterministic reference value plus Maxio's reference-uniqueness constraint
    /// make this safe under concurrent calls.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(string customerReference, string userName, string email, CancellationToken cancellationToken)
    {
        var existing = await _apiClient.LookupCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(userName);
        try
        {
            var created = await _apiClient.CreateCustomerAsync(
                new MaxioCreateCustomerBody
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = customerReference
                },
                cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} (reference {Reference}).", created.Id, customerReference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.UpstreamStatusCode == 422)
        {
            // Another concurrent request likely won the race to create the customer
            // (Maxio enforces one customer per reference). Re-read and return it.
            var raced = await _apiClient.LookupCustomerByReferenceAsync(customerReference, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation("Customer creation raced with a concurrent request; reusing customer {CustomerId} (reference {Reference}).", raced.Id, customerReference);
                return raced;
            }

            throw;
        }
    }

    internal static string BuildCustomerReference(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A user id is required to resolve the billing customer.", nameof(userId));
        }

        return CustomerReferencePrefix + userId.Trim();
    }

    internal static (string FirstName, string LastName) SplitName(string userName)
    {
        var sanitized = string.IsNullOrWhiteSpace(userName) ? "shopper" : userName.Trim();
        // Maxio requires first/last names; derive both from the username, keeping
        // each within a conservative length so the payload always validates.
        var first = Truncate(sanitized, 20);
        var last = "eShop Customer";
        return (first, last);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static SubscriptionDetails Map(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        NextBillingDateUtc = subscription.CurrentPeriodEndsAt,
        ActivatedAtUtc = subscription.ActivatedAt,
        CreatedAtUtc = subscription.CreatedAt
    };
}
