using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Subscribe flow on top of the billing system of record. The eShop user name is the billing customer
/// reference, so no local mapping is persisted. Register as a singleton (holds per-user locks).
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private static readonly string[] _deadStates = { "canceled", "expired", "failed_to_create" };

    private readonly IBillingGateway _gateway;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new(StringComparer.OrdinalIgnoreCase);

    public SubscriptionService(IBillingGateway gateway)
    {
        _gateway = gateway;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken ct = default)
        => _gateway.ListPlansAsync(ct);

    public async Task<SubscribeResult> SubscribeAsync(string userName, string? email, string planHandle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("User name is required.", nameof(userName));
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new BillingException(BillingFailureKind.Validation, "A plan handle is required.");

        planHandle = planHandle.Trim();

        // Serialise per user so a double-click cannot race two creates past the duplicate check.
        // (Single-instance guarantee; across instances the customer reference and the live-subscription
        // check below are the safeguards.)
        var userLock = _userLocks.GetOrAdd(userName, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(ct);
        try
        {
            var plans = await _gateway.ListPlansAsync(ct);
            if (!plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
                throw new BillingException(BillingFailureKind.NotFound, $"Plan '{planHandle}' was not found.");

            var customerId = await EnsureCustomerAsync(userName, email, ct);

            var existing = FindLive(await _gateway.ListSubscriptionsAsync(customerId, ct), planHandle);
            if (existing != null)
                return new SubscribeResult(existing, false);

            try
            {
                var created = await _gateway.CreateSubscriptionAsync(customerId, planHandle, ct);
                return new SubscribeResult(created, true);
            }
            catch (BillingException ex) when (ex.Kind == BillingFailureKind.Unavailable)
            {
                // The write may have reached the billing system before the connection failed (and the SDK
                // may have resent it). Re-read state rather than assume nothing happened.
                var reconciled = FindLive(await _gateway.ListSubscriptionsAsync(customerId, ct), planHandle);
                if (reconciled != null)
                    return new SubscribeResult(reconciled, true);
                throw;
            }
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> GetMySubscriptionsAsync(string userName, CancellationToken ct = default)
    {
        var customerId = await _gateway.FindCustomerIdAsync(userName, ct);
        if (customerId == null)
            return Array.Empty<SubscriptionInfo>();
        return await _gateway.ListSubscriptionsAsync(customerId.Value, ct);
    }

    private async Task<int> EnsureCustomerAsync(string userName, string? email, CancellationToken ct)
    {
        var id = await _gateway.FindCustomerIdAsync(userName, ct);
        if (id != null)
            return id.Value;

        var emailAddress = string.IsNullOrWhiteSpace(email) ? userName : email;
        var local = emailAddress.Split('@')[0];
        var customer = new BillingCustomer(userName, emailAddress, string.IsNullOrWhiteSpace(local) ? userName : local, "Customer");
        try
        {
            return await _gateway.CreateCustomerAsync(customer, ct);
        }
        catch (BillingException ex) when (ex.Kind is BillingFailureKind.Validation or BillingFailureKind.Unavailable)
        {
            // A concurrent request (or a resent write) may have created the customer: look again before failing.
            var again = await _gateway.FindCustomerIdAsync(userName, ct);
            if (again != null)
                return again.Value;
            throw;
        }
    }

    private static SubscriptionInfo? FindLive(IEnumerable<SubscriptionInfo> subscriptions, string planHandle)
        => subscriptions.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase)
            && !_deadStates.Contains(s.State, StringComparer.OrdinalIgnoreCase));
}
