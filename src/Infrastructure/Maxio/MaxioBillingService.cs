using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing against Maxio Advanced Billing (the billing system of record).
/// All interactions use the endpoints and schemas of maxio-spec/openapi.yaml.
/// Idempotency: the Maxio customer reference is the application's stable user reference, and each
/// subscription carries a deterministic reference "{userId}:{planHandle}", so repeated
/// subscribe calls (double-clicks, restarts) never create duplicate records.
/// </summary>
public sealed class MaxioBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// Subscription states in which a plan is considered already taken for the user,
    /// per the spec's Subscription State model (live and problem states). End-of-life
    /// states (canceled, expired, unpaid, ...) let the user subscribe again.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "past_due", "soft_failure", "suspended"
    };

    private const int PageSize = 200;
    private const int MaxPages = 25;

    private readonly MaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly MaxioUserLockRegistry _userLocks;

    public MaxioBillingService(MaxioApiClient client, IOptions<MaxioOptions> options, ILogger<MaxioBillingService> logger, MaxioUserLockRegistry userLocks)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
        _userLocks = userLocks;
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await ResolveProductFamilyAsync(cancellationToken);

        var products = await PagedAsync<MaxioProductEnvelope>($"product_families/{family.Id}/products.json",
            page => new Dictionary<string, string> { ["page"] = page.ToString(), ["per_page"] = PageSize.ToString() },
            cancellationToken);

        var plans = products
            .Where(p => !string.IsNullOrWhiteSpace(p.Product?.Handle) && string.IsNullOrWhiteSpace(p.Product.ArchivedAt))
            .Select(p => new SubscriptionPlanInfo
            {
                Handle = p.Product!.Handle!,
                Name = p.Product.Name ?? p.Product.Handle!,
                Description = p.Product.Description,
                PriceInCents = p.Product.PriceInCents,
                Interval = p.Product.Interval,
                IntervalUnit = p.Product.IntervalUnit ?? "month",
                ProductFamilyHandle = family.Handle ?? _options.ProductFamilyHandle,
                PaymentMethodRequired = p.Product.RequireCreditCard ?? false
            })
            .ToList();

        _logger.LogInformation("Maxio billing: found {PlanCount} subscribable plans in product family {FamilyHandle}", plans.Count, family.Handle);
        return plans;
    }

    public async Task<SubscriptionInfo> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        var userLock = _userLocks.GetLockForKey(command.UserReference);
        await userLock.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeLockedAsync(command, cancellationToken);
        }
        finally
        {
            userLock.Release();
        }
    }

    private async Task<SubscriptionInfo> SubscribeLockedAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, command.PlanHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new InvalidOperationException(
                $"Plan '{command.PlanHandle}' is not available for subscription. Available plans: {string.Join(", ", plans.Select(p => p.Handle))}");
        }

        var customer = await EnsureCustomerAsync(command, cancellationToken);

        // Idempotency: a deterministic subscription reference keyed by user + plan.
        var deterministicReference = BuildSubscriptionReference(command.UserReference, plan.Handle);
        var subscriptionReference = deterministicReference;

        var existing = await _client.GetOptionalAsync<MaxioSubscriptionEnvelope>(
            "subscriptions/lookup.json",
            new Dictionary<string, string> { ["reference"] = subscriptionReference },
            cancellationToken);

        if (existing?.Subscription is not null)
        {
            if (LiveStates.Contains(existing.Subscription.State ?? string.Empty))
            {
                _logger.LogInformation("Maxio billing: user {UserReference} already holds subscription {SubscriptionId} for plan {PlanHandle}",
                    command.UserReference, existing.Subscription.Id, plan.Handle);
                return MapSubscription(existing.Subscription, alreadySubscribed: true);
            }

            // The deterministic reference is held by an end-of-life subscription; the user
            // may resubscribe. First adopt any other live subscription for the same plan.
            // Advanced Billing list endpoints can briefly lag behind a concurrent create,
            // so retry the adoption probe for a couple of seconds before creating anew.
            var adopted = await FindLiveSubscriptionWithRetryAsync(customer.Id, plan.Handle, cancellationToken);
            if (adopted is not null)
            {
                return MapSubscription(adopted, alreadySubscribed: true);
            }

            // Uniquely suffix the reference for the new subscription attempt.
            subscriptionReference = $"{deterministicReference}:{Guid.NewGuid().ToString("N")[..12]}";
        }

        try
        {
            return await CreateSubscriptionAsync(customer.Id, plan.Handle, subscriptionReference, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422 && ex.IsDuplicateReferenceError())
        {
            // Rare race: another create for this user/plan completed between our checks
            // (e.g. its listing was not visible yet). Adopt the winner instead of failing.
            var winner = await FindLiveSubscriptionWithRetryAsync(customer.Id, plan.Handle, cancellationToken);
            if (winner is not null)
            {
                _logger.LogInformation("Maxio billing: adopted subscription {SubscriptionId} for user {UserReference} on plan {PlanHandle} after a create race",
                    winner.Id, command.UserReference, plan.Handle);
                return MapSubscription(winner, alreadySubscribed: true);
            }
            throw;
        }
    }

    private async Task<SubscriptionInfo> CreateSubscriptionAsync(long customerId, string planHandle, string subscriptionReference, CancellationToken cancellationToken)
    {
        var created = await _client.PostAsync<MaxioSubscriptionEnvelope>(
            "subscriptions.json",
            new MaxioCreateSubscriptionEnvelope
            {
                Subscription = new MaxioCreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    Reference = subscriptionReference,
                    // Card-less plans activate without a payment profile under invoice billing.
                    PaymentCollectionMethod = "remittance"
                }
            },
            cancellationToken);

        if (created.Subscription is null)
        {
            throw new MaxioApiException(500, "Maxio returned an empty subscription on create.", Array.Empty<string>());
        }

        _logger.LogInformation("Maxio billing: created subscription {SubscriptionId} on plan {PlanHandle}",
            created.Subscription.Id, planHandle);
        return MapSubscription(created.Subscription, alreadySubscribed: false);
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var customer = await _client.GetOptionalAsync<MaxioCustomerEnvelope>(
            "customers/lookup.json",
            new Dictionary<string, string> { ["reference"] = userId },
            cancellationToken);

        if (customer?.Customer is null)
        {
            return Array.Empty<SubscriptionInfo>();
        }

        var subscriptions = await PagedAsync<MaxioSubscriptionEnvelope>($"customers/{customer.Customer.Id}/subscriptions.json",
            page => new Dictionary<string, string> { ["page"] = page.ToString(), ["per_page"] = PageSize.ToString() },
            cancellationToken);

        return subscriptions
            .Where(s => s.Subscription is not null)
            .Select(s => MapSubscription(s.Subscription!, alreadySubscribed: false))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>
    /// Ensures a Maxio customer exists for the application user. The customer reference is
    /// the stable user reference, so lookups are stable across restarts and never create duplicates.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var existing = await _client.GetOptionalAsync<MaxioCustomerEnvelope>(
            "customers/lookup.json",
            new Dictionary<string, string> { ["reference"] = command.UserReference },
            cancellationToken);

        if (existing?.Customer is not null)
        {
            return existing.Customer;
        }

        try
        {
            var created = await _client.PostAsync<MaxioCustomerEnvelope>(
                "customers.json",
                new MaxioCreateCustomerEnvelope
                {
                    Customer = new MaxioCreateCustomer
                    {
                        FirstName = command.FirstName,
                        LastName = command.LastName,
                        Email = command.Email,
                        Reference = command.UserReference
                    }
                },
                cancellationToken);

            if (created.Customer is null)
            {
                throw new MaxioApiException(500, "Maxio returned an empty customer on create.", Array.Empty<string>());
            }

            _logger.LogInformation("Maxio billing: created customer {CustomerId} for user {UserId}", created.Customer.Id, command.UserReference);
            return created.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422 && ex.IsDuplicateReferenceError())
        {
            // Lost a race against another creator for the same reference: adopt the winner.
            var winner = await _client.GetOptionalAsync<MaxioCustomerEnvelope>(
                "customers/lookup.json",
                new Dictionary<string, string> { ["reference"] = command.UserReference },
                cancellationToken);

            if (winner?.Customer is not null)
            {
                return winner.Customer;
            }
            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await PagedAsync<MaxioSubscriptionEnvelope>($"customers/{customerId}/subscriptions.json",
            page => new Dictionary<string, string> { ["page"] = page.ToString(), ["per_page"] = PageSize.ToString() },
            cancellationToken);

        return subscriptions.FirstOrDefault(s =>
            s.Subscription is not null &&
            LiveStates.Contains(s.Subscription.State ?? string.Empty) &&
            string.Equals(s.Subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))?.Subscription;
    }

    /// <summary>
    /// Probes for a live subscription of the plan, retrying briefly to absorb the
    /// read-after-write lag of the Advanced Billing list endpoints.
    /// </summary>
    private async Task<MaxioSubscription?> FindLiveSubscriptionWithRetryAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(750, cancellationToken);
            }
            var found = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
            if (found is not null)
            {
                return found;
            }
        }
        return null;
    }

    private async Task<MaxioProductFamily> ResolveProductFamilyAsync(CancellationToken cancellationToken)
    {
        var handle = _options.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: Maxio:ProductFamilyHandle is required (source: MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }

        var families = await PagedAsync<MaxioProductFamilyEnvelope>("product_families.json",
            page => new Dictionary<string, string> { ["page"] = page.ToString(), ["per_page"] = PageSize.ToString() },
            cancellationToken);

        var family = families.FirstOrDefault(f =>
            string.Equals(f.ProductFamily?.Handle, handle, StringComparison.OrdinalIgnoreCase))?.ProductFamily;

        if (family is null)
        {
            throw new InvalidOperationException(
                $"Maxio product family '{handle}' was not found on the site. Check Maxio:ProductFamilyHandle.");
        }

        return family;
    }

    /// <summary>
    /// Pages a Maxio list endpoint (array of envelopes) until exhausted or MaxPages is hit.
    /// </summary>
    private async Task<List<T>> PagedAsync<T>(string path, Func<int, Dictionary<string, string>> queryParams, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var batch = await _client.GetOptionalAsync<List<T>>(path, queryParams(page), cancellationToken)
                        ?? new List<T>();
            results.AddRange(batch);
            if (batch.Count < PageSize)
            {
                break;
            }
        }
        return results;
    }

    private static string BuildSubscriptionReference(string userId, string planHandle) =>
        $"eshopweb:{userId}:{planHandle}".ToLowerInvariant();

    private static SubscriptionInfo MapSubscription(MaxioSubscription subscription, bool alreadySubscribed) =>
        new()
        {
            SubscriptionId = subscription.Id,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            NextBillingDate = ParseDate(subscription.CurrentPeriodEndsAt) ?? ParseDate(subscription.NextAssessmentAt),
            ActivatedAt = ParseDate(subscription.ActivatedAt),
            CreatedAt = ParseDate(subscription.CreatedAt),
            AlreadySubscribed = alreadySubscribed
        };

    private static DateTimeOffset? ParseDate(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);
}
