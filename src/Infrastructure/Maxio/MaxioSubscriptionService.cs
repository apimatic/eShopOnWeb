using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates Maxio calls to implement <see cref="IMaxioSubscriptionService"/>. Maxio is the
/// system of record for subscriptions: nothing about plans or subscriptions is cached or mirrored
/// locally, so results are always as fresh as Maxio itself and nothing is lost if this process
/// restarts (unlike the app's in-memory EF database).
///
/// Idempotency (a double-click must never create two customers or two subscriptions) is layered:
///  1. Customers are looked up by reference (the eShopOnWeb username) before ever creating one;
///     a 422 "reference taken" on create (a create/create race) falls back to a re-lookup.
///  2. An in-process, per-(user, plan) async lock serializes concurrent SubscribeAsync calls for
///     the same key, so the "check, then create" logic below can never race with itself within
///     this instance - this is what actually closes the double-click window, deterministically.
///  3. Before creating a subscription, existing live subscriptions to the same plan are checked
///     for and returned as-is instead of creating a duplicate.
///  4. The create-subscription call also carries a uniqueness_token derived from (username, plan
///     handle, a coarse time bucket). This is defense-in-depth for a multi-instance deployment
///     (where #2's in-process lock wouldn't span instances): two truly-concurrent requests land in
///     the same bucket and Maxio itself 409s the second one (see Maxio's "Duplicate Prevention"
///     docs), falling back to re-reading the customer's subscriptions. The token is bucketed
///     rather than permanently deterministic so that a legitimate resubscribe long after a prior
///     cancellation doesn't collide with a stale token still inside Maxio's 60-minute window.
/// </summary>
internal class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "past_due", "soft_failure", "unpaid", "assessing", "pending", "awaiting_signup"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly IMaxioClient _client;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(IMaxioClient client, IOptions<MaxioOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products.Select(ToPlan).ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string username, string planHandle, CancellationToken cancellationToken = default)
    {
        var subscribeLock = SubscribeLocks.GetOrAdd($"{username}:{planHandle}".ToLowerInvariant(), _ => new SemaphoreSlim(1, 1));
        await subscribeLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(username, cancellationToken);

            var existing = await FindLiveSubscriptionForPlanAsync(customer.Id, planHandle, cancellationToken);
            if (existing is not null)
            {
                return new SubscribeResult(ToSubscription(existing), IsNewSubscription: false);
            }

            var uniquenessToken = BuildUniquenessToken(username, planHandle);
            var created = await _client.CreateSubscriptionAsync(customer.Id, planHandle, uniquenessToken, cancellationToken);

            if (created is null)
            {
                // Maxio reported the uniqueness_token as a duplicate submission: another request for
                // the same user+plan (from a different instance) is creating it. Read it back.
                var afterRace = await FindLiveSubscriptionForPlanAsync(customer.Id, planHandle, cancellationToken);
                if (afterRace is null)
                {
                    throw new MaxioApiException(HttpStatusCode.Conflict,
                        $"Maxio reported a duplicate subscribe request for plan '{planHandle}', but no matching subscription could be found afterwards.");
                }

                return new SubscribeResult(ToSubscription(afterRace), IsNewSubscription: false);
            }

            return new SubscribeResult(ToSubscription(created), IsNewSubscription: true);
        }
        finally
        {
            subscribeLock.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string username, CancellationToken cancellationToken = default)
    {
        var customer = await _client.LookupCustomerByReferenceAsync(username, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToSubscription).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string username, CancellationToken cancellationToken)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(username, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var (firstName, lastName) = DeriveDisplayName(username);
            return await _client.CreateCustomerAsync(firstName, lastName, email: username, reference: username, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Another concurrent request for the same user created the customer first.
            var afterRace = await _client.LookupCustomerByReferenceAsync(username, cancellationToken);
            if (afterRace is not null)
            {
                return afterRace;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionForPlanAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            LiveStates.Contains(s.State));
    }

    private static (string FirstName, string LastName) DeriveDisplayName(string username)
    {
        var localPart = username.Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;
        return (firstName, "eShopOnWeb Customer");
    }

    /// <summary>
    /// Deterministic within a short window (not random) on purpose: two truly-concurrent subscribe
    /// requests for the same user+plan, arriving less than a minute apart, land on the same token
    /// so Maxio's own duplicate-submission window catches races that span multiple app instances
    /// (which <see cref="SubscribeLocks"/> cannot). Bucketing by time - rather than being
    /// permanently deterministic per (user, plan) - means a legitimate resubscribe minutes later
    /// gets a fresh token instead of colliding with a stale one from an earlier, unrelated attempt.
    /// </summary>
    private static string BuildUniquenessToken(string username, string planHandle)
    {
        var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var input = $"eshoponweb-subscribe:{username}:{planHandle}:{bucket}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new(
        Handle: product.Handle,
        Name: product.Name,
        Description: product.Description,
        PriceInCents: product.PriceInCents,
        IntervalCount: product.Interval,
        IntervalUnit: product.IntervalUnit);

    private static CustomerSubscription ToSubscription(MaxioSubscription subscription) => new(
        SubscriptionId: subscription.Id,
        PlanHandle: subscription.Product?.Handle ?? string.Empty,
        PlanName: subscription.Product?.Name ?? string.Empty,
        PriceInCents: subscription.ProductPriceInCents,
        State: subscription.State,
        CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
        NextBillingAt: subscription.NextAssessmentAt,
        CreatedAt: subscription.CreatedAt);
}
