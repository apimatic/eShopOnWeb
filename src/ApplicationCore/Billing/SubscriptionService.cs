using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public class SubscriptionService(ISubscriptionGateway gateway, IBillingClaimStore store, ILogger<SubscriptionService> logger)
{
    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        return await gateway.GetPlansAsync(deadline.Token);
    }

    public async Task<EnrollmentResult> SubscribeAsync(BillingAccount account, string handle, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        // Independent of a disconnected shopper, but capped at 30 seconds from request start.
        using var recoveryBudget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var plans = await gateway.GetPlansAsync(token);
        if (!plans.Any(x => x.Handle == handle))
            throw new BillingException("plan_not_offered", "Choose a handle returned by subscription-plans.", 400);
        var customer = await EnsureCustomerAsync(account, token, recoveryBudget.Token);
        if (customer is null)
        {
            // Preserve the enrollment intent while the customer write is being reconciled.
            var intent = Claim(account.UserId, "Subscription", handle);
            intent.State = "AwaitingCustomer";
            await store.AcquireAsync(intent, token);
            return new EnrollmentResult("pending", null);
        }

        var candidate = Claim(account.UserId, "Subscription", handle);
        candidate.State = "AwaitingCustomer";
        var (acquired, claim) = await store.AcquireAsync(candidate, token);
        OwnedSubscription? previous;
        try { previous = await RecoverSubscriptionAsync(claim, customer, token); }
        catch
        {
            // No write was sent by this contender, so its new claim can safely be released.
            if (acquired)
            {
                using var cleanup = RecoveryDeadline(recoveryBudget.Token);
                await store.ReleaseAsync(claim.Id, cleanup.Token, "AwaitingCustomer");
            }
            throw;
        }
        if (previous is not null) return new EnrollmentResult("confirmed", previous.Details);
        if (!await store.TryBeginEnrollmentAsync(claim.Id, token)) return new EnrollmentResult("pending", null);
        return await CreateEnrollmentAsync(claim, customer, token, recoveryBudget.Token);
    }

    private async Task<EnrollmentResult> CreateEnrollmentAsync(BillingClaim claim, BillingCustomer customer, CancellationToken token, CancellationToken recoveryToken)
    {
        try
        {
            var created = await gateway.CreateSubscriptionAsync(customer.Id, claim.ProductHandle, claim.Reference, token);
            Verify(created, customer, claim);
            await store.CompleteAsync(claim.Id, created.Details.Id, token);
            return new EnrollmentResult("confirmed", created.Details);
        }
        catch (BillingException ex) when (!ex.OutcomeUnknown)
        {
            using var cleanup = RecoveryDeadline(recoveryToken);
            await store.ReleaseAsync(claim.Id, cleanup.Token);
            throw;
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            return await RecoverUnknownSubscriptionAsync(claim, customer, recoveryToken);
        }
    }

    private async Task<BillingCustomer?> EnsureCustomerAsync(BillingAccount account, CancellationToken ct, CancellationToken recoveryToken)
    {
        var (acquired, claim) = await store.AcquireAsync(Claim(account.UserId, "Customer", ""), ct);
        BillingCustomer? found;
        try { found = await gateway.FindCustomerAsync(claim.Reference, ct); }
        catch
        {
            if (acquired)
            {
                using var cleanup = RecoveryDeadline(recoveryToken);
                await store.ReleaseAsync(claim.Id, cleanup.Token);
            }
            throw;
        }
        if (found is not null)
        {
            await store.CompleteAsync(claim.Id, found.Id, ct);
            return found;
        }
        if (!acquired) return null;
        try
        {
            var created = await gateway.CreateCustomerAsync(account, claim.Reference, ct);
            await store.CompleteAsync(claim.Id, created.Id, ct);
            return created;
        }
        catch (BillingException ex) when (!ex.OutcomeUnknown)
        {
            using var cleanup = RecoveryDeadline(recoveryToken);
            // A concurrent actor outside this app may have created the unique reference.
            if (ex.StatusCode is 409 or 422)
            {
                var recovered = await gateway.FindCustomerAsync(claim.Reference, cleanup.Token);
                if (recovered is not null)
                {
                    await store.CompleteAsync(claim.Id, recovered.Id, cleanup.Token);
                    return recovered;
                }
            }
            await store.ReleaseAsync(claim.Id, cleanup.Token);
            throw;
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            using var recovery = RecoveryDeadline(recoveryToken);
            await store.MarkUnknownAsync(claim.Id, recovery.Token);
            logger.LogWarning("Customer billing outcome unknown for claim {ClaimId}", claim.Id);
            try
            {
                var recovered = await gateway.FindCustomerAsync(claim.Reference, recovery.Token);
                if (recovered is null) return null;
                await store.CompleteAsync(claim.Id, recovered.Id, recovery.Token);
                return recovered;
            }
            catch (Exception failure) when (failure is BillingException or OperationCanceledException) { return null; }
        }
    }

    private async Task<EnrollmentResult> RecoverUnknownSubscriptionAsync(BillingClaim claim, BillingCustomer customer, CancellationToken recoveryToken)
    {
        using var recovery = RecoveryDeadline(recoveryToken);
        await store.MarkUnknownAsync(claim.Id, recovery.Token);
        logger.LogWarning("Subscription billing outcome unknown for claim {ClaimId}", claim.Id);
        try
        {
            var recovered = await RecoverSubscriptionAsync(claim, customer, recovery.Token);
            return new EnrollmentResult(recovered is null ? "pending" : "confirmed", recovered?.Details);
        }
        catch (Exception failure) when (failure is BillingException or OperationCanceledException) { return new EnrollmentResult("pending", null); }
    }

    private async Task<OwnedSubscription?> RecoverSubscriptionAsync(BillingClaim claim, BillingCustomer customer, CancellationToken ct)
    {
        var sub = await gateway.FindSubscriptionAsync(claim.Reference, ct);
        if (sub is null)
        {
            var subscriptions = await gateway.GetSubscriptionsAsync(customer.Id, ct);
            sub = subscriptions.SingleOrDefault(x => x.Reference == claim.Reference);
        }
        if (sub is null) return null;
        Verify(sub, customer, claim);
        await store.CompleteAsync(claim.Id, sub.Details.Id, ct);
        return sub;
    }

    private static void Verify(OwnedSubscription sub, BillingCustomer customer, BillingClaim claim)
    {
        if (sub.CustomerId != customer.Id || sub.CustomerReference != customer.Reference ||
            sub.Reference != claim.Reference || sub.Details.ProductHandle != claim.ProductHandle)
            throw new BillingException("billing_ownership_mismatch", "Billing returned an inconsistent enrollment.", outcomeUnknown: true);
    }

    public async Task<MySubscriptions> GetMineAsync(string userId, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        var token = deadline.Token;
        await ReconcileUserAsync(userId, token);
        var customer = await gateway.FindCustomerAsync(Claim(userId, "Customer", "").Reference, token);
        var details = new List<SubscriptionDetails>();
        if (customer is not null)
        {
            foreach (var sub in await gateway.GetSubscriptionsAsync(customer.Id, token))
            {
                if (sub.CustomerId != customer.Id || sub.CustomerReference != customer.Reference)
                    throw new BillingException("billing_ownership_mismatch", "Billing returned inconsistent account information.");
                // Only this application's configured-family enrollments belong in this account view.
                if (sub.Details.ProductHandle is not null &&
                    sub.Reference == Claim(userId, "Subscription", sub.Details.ProductHandle).Reference)
                    details.Add(sub.Details);
            }
        }
        var pending = await store.GetPendingAsync(gateway.Scope, userId, token);
        return new MySubscriptions(details, pending.Select(x => new PendingEnrollment(x.ProductHandle, "pending")).ToList());
    }

    private async Task ReconcileUserAsync(string userId, CancellationToken ct)
    {
        var pending = await store.GetPendingAsync(gateway.Scope, userId, ct);
        if (pending.Count == 0) return;
        var customer = await gateway.FindCustomerAsync(Claim(userId, "Customer", "").Reference, ct);
        if (customer is null) return;
        foreach (var claim in pending)
        {
            if (claim.Kind == "Customer") await store.CompleteAsync(claim.Id, customer.Id, ct);
            else
            {
                var recovered = await RecoverSubscriptionAsync(claim, customer, ct);
                if (recovered is null && claim.State == "AwaitingCustomer")
                {
                    var plans = await gateway.GetPlansAsync(ct);
                    if (!plans.Any(x => x.Handle == claim.ProductHandle))
                    {
                        await store.ReleaseAsync(claim.Id, ct, "AwaitingCustomer");
                        continue;
                    }
                    if (await store.TryBeginEnrollmentAsync(claim.Id, ct))
                    {
                        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await CreateEnrollmentAsync(claim, customer, ct, recovery.Token);
                    }
                }
            }
        }
    }

    // A sweep may start a claimed intent that was awaiting its customer. It never
    // resends a Pending/Unknown write, even after a process restart.
    public async Task ReconcileAsync(CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        var pending = await store.GetUnsettledAsync(gateway.Scope, deadline.Token);
        foreach (var user in pending.Select(x => x.UserId).Distinct())
            await ReconcileUserAsync(user, deadline.Token);
    }

    private BillingClaim Claim(string userId, string kind, string handle)
    {
        string key = Hash($"{gateway.Scope}\n{userId}\n{kind}\n{(kind == "Customer" ? "" : gateway.ProductFamilyHandle)}\n{handle}");
        return new BillingClaim { Id = key, Scope = gateway.Scope, UserId = userId, Kind = kind,
            ProductHandle = handle, Reference = "eshop-" + key };
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static CancellationTokenSource RecoveryDeadline(CancellationToken totalBudget)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(totalBudget);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        return deadline;
    }
    private static CancellationTokenSource Deadline(CancellationToken ct)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        return deadline;
    }
}
