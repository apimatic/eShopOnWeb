using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Recurring-subscription use cases. Order for every write: claim (store) -> provider call -> record result.
/// The whole-call time budget of each use case lives here, in one place (<see cref="Bounded{T}"/>).
/// </summary>
public class SubscriptionService
{
    /// <summary>Plan used when the caller names none (still subject to the "offered plan" check).</summary>
    public const string DefaultPlanHandle = "eshop-pro";

    public static readonly TimeSpan SubscribeBudget = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(15);
    /// <summary>How long a claim in state Claimed is considered to belong to a request still running.</summary>
    public static readonly TimeSpan InFlightWindow = TimeSpan.FromSeconds(60);

    private const int CustomerPollAttempts = 5;
    private const string FallbackLastName = "Shopper";

    private readonly IBillingGateway _gateway;
    private readonly IBillingClaimStore _claims;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly TimeProvider _time;
    private readonly TimeSpan _customerPollDelay;

    public SubscriptionService(IBillingGateway gateway,
        IBillingClaimStore claims,
        IAppLogger<SubscriptionService> logger,
        TimeProvider? timeProvider = null,
        TimeSpan? customerPollDelay = null)
    {
        _gateway = gateway;
        _claims = claims;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        _customerPollDelay = customerPollDelay ?? TimeSpan.FromMilliseconds(500);
    }

    public static string NormalizeUser(string userName)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        return userName.Trim().ToLowerInvariant();
    }

    public static string CustomerReference(string normalizedUser) => $"eshop-user:{normalizedUser}";

    public static string SubscriptionReference(string normalizedUser, string planHandle) =>
        $"eshop-sub:{normalizedUser}:{planHandle}";

    public Task<PlanListResult> ListPlansAsync(CancellationToken ct) =>
        Bounded(ReadBudget, ct, token => _gateway.ListPlansAsync(token));

    public Task<IReadOnlyList<SubscriptionInfo>> ListMySubscriptionsAsync(BillingUser user, CancellationToken ct) =>
        Bounded(ReadBudget, ct, async token =>
        {
            var userId = NormalizeUser(user.UserName);
            var customer = await _gateway.FindCustomerByReferenceAsync(CustomerReference(userId), token);
            if (customer is null)
            {
                return (IReadOnlyList<SubscriptionInfo>)Array.Empty<SubscriptionInfo>();
            }
            return await _gateway.ListCustomerSubscriptionsAsync(customer.Id, token);
        });

    public Task<SubscribeResult> SubscribeAsync(BillingUser user, string? planHandle, CancellationToken ct) =>
        Bounded(SubscribeBudget, ct, token => SubscribeCoreAsync(user, planHandle, token));

    private async Task<SubscribeResult> SubscribeCoreAsync(BillingUser user, string? requestedPlan, CancellationToken ct)
    {
        var userId = NormalizeUser(user.UserName);
        var wanted = string.IsNullOrWhiteSpace(requestedPlan) ? DefaultPlanHandle : requestedPlan.Trim();

        // Invariant: only a plan the configured product family offers may be subscribed to.
        var listing = await _gateway.ListPlansAsync(ct);
        PlanInfo? plan = null;
        foreach (var candidate in listing.Plans)
        {
            if (string.Equals(candidate.Handle, wanted, StringComparison.OrdinalIgnoreCase))
            {
                plan = candidate;
                break;
            }
        }
        if (plan is null)
        {
            if (listing.IsTruncated)
            {
                throw new BillingProviderException(BillingFailureKind.Unavailable,
                    "The plan listing is incomplete; cannot verify the requested plan.");
            }
            _logger.LogWarning("Subscribe rejected: plan {Plan} is not offered", wanted);
            throw new PlanNotOfferedException();
        }

        return await ClaimAndSubscribeAsync(user, userId, plan.Handle, allowTakeover: true, ct);
    }

    private async Task<SubscribeResult> ClaimAndSubscribeAsync(BillingUser user, string userId, string planHandle,
        bool allowTakeover, CancellationToken ct)
    {
        var subscriptionReference = SubscriptionReference(userId, planHandle);

        // 1. claim
        var claimResult = await _claims.TryClaimSubscriptionAsync(userId, planHandle, ct);
        if (!claimResult.Acquired)
        {
            var existing = claimResult.Claim;
            if (existing.State == ClaimState.Completed)
            {
                var found = await _gateway.FindSubscriptionByReferenceAsync(subscriptionReference, ct);
                if (found is null)
                {
                    throw new BillingProviderException(BillingFailureKind.Unavailable,
                        "The existing subscription could not be read back from the billing provider.");
                }
                _logger.LogInformation("Subscribe: existing subscription {SubscriptionId} returned for plan {Plan}",
                    found.SubscriptionId, planHandle);
                return new SubscribeResult(found, Created: false);
            }

            var age = _time.GetUtcNow().UtcDateTime - existing.ClaimedAtUtc;
            if (existing.State == ClaimState.Claimed && age < InFlightWindow)
            {
                _logger.LogWarning("Subscribe claim conflict for plan {Plan}: request already in progress", planHandle);
                throw new SubscriptionInProgressException();
            }

            // Claimed (stale) or Unknown: settle by re-reading the provider by the deterministic reference.
            var settled = await _gateway.FindSubscriptionByReferenceAsync(subscriptionReference, ct);
            if (settled is not null)
            {
                await _claims.CompleteSubscriptionAsync(userId, planHandle, settled.SubscriptionId, CancellationToken.None);
                _logger.LogWarning("Subscribe reconciled unsettled claim for plan {Plan} to subscription {SubscriptionId}",
                    planHandle, settled.SubscriptionId);
                return new SubscribeResult(settled, Created: false);
            }

            if (!allowTakeover)
            {
                throw new SubscriptionInProgressException();
            }
            // Provider has no such subscription: the old attempt never landed. Drop the stale claim and claim afresh.
            await _claims.ReleaseSubscriptionAsync(userId, planHandle, existing.ClaimedAtUtc, CancellationToken.None);
            return await ClaimAndSubscribeAsync(user, userId, planHandle, allowTakeover: false, ct);
        }

        var claimedAt = claimResult.Claim.ClaimedAtUtc;
        var writeAttempted = false;
        try
        {
            // 2. provider calls
            var customerId = await EnsureCustomerAsync(user, userId, ct);

            var info = await _gateway.FindSubscriptionByReferenceAsync(subscriptionReference, ct);
            var created = false;
            if (info is null)
            {
                writeAttempted = true;
                info = await _gateway.CreateSubscriptionAsync(customerId, planHandle, subscriptionReference, ct);
                created = true;
            }

            // 3. record
            await _claims.CompleteSubscriptionAsync(userId, planHandle, info.SubscriptionId, CancellationToken.None);
            _logger.LogInformation("Subscribe: subscription {SubscriptionId} for plan {Plan} (created: {Created})",
                info.SubscriptionId, planHandle, created);
            return new SubscribeResult(info, created);
        }
        catch (Exception ex)
        {
            var definite = ex is BillingProviderRejectedException
                           || ex is BillingProviderException { Kind: BillingFailureKind.ConfigFault or BillingFailureKind.Unavailable }
                           || ex is BillingRequestException;
            if (!writeAttempted || definite)
            {
                await _claims.ReleaseSubscriptionAsync(userId, planHandle, claimedAt, CancellationToken.None);
            }
            else
            {
                _logger.LogWarning("Subscribe outcome for plan {Plan} is unknown; claim kept as Unknown for reconciliation", planHandle);
                await _claims.MarkSubscriptionUnknownAsync(userId, planHandle, CancellationToken.None);
            }
            throw;
        }
    }

    private async Task<int> EnsureCustomerAsync(BillingUser user, string userId, CancellationToken ct)
    {
        var reference = CustomerReference(userId);
        var existing = await _gateway.FindCustomerByReferenceAsync(reference, ct);
        if (existing is not null)
        {
            return existing.Id;
        }

        var email = ResolveEmail(user);
        return await ClaimAndCreateCustomerAsync(userId, reference, email, allowTakeover: true, ct);
    }

    private async Task<int> ClaimAndCreateCustomerAsync(string userId, string reference, string email, bool allowTakeover, CancellationToken ct)
    {
        var claimResult = await _claims.TryClaimCustomerAsync(userId, ct);
        if (!claimResult.Acquired)
        {
            // Another request is creating (or an earlier attempt died): wait for the customer to appear.
            for (var i = 0; i < CustomerPollAttempts; i++)
            {
                await Task.Delay(_customerPollDelay, _time, ct);
                var appeared = await _gateway.FindCustomerByReferenceAsync(reference, ct);
                if (appeared is not null)
                {
                    return appeared.Id;
                }
            }

            var age = _time.GetUtcNow().UtcDateTime - claimResult.Claim.ClaimedAtUtc;
            if (allowTakeover && age >= InFlightWindow)
            {
                await _claims.ReleaseCustomerAsync(userId, claimResult.Claim.ClaimedAtUtc, CancellationToken.None);
                return await ClaimAndCreateCustomerAsync(userId, reference, email, allowTakeover: false, ct);
            }
            throw new SubscriptionInProgressException();
        }

        try
        {
            var localPart = email.Substring(0, email.IndexOf('@'));
            var firstName = string.IsNullOrWhiteSpace(localPart) ? "Customer" : localPart;
            var customer = await _gateway.CreateCustomerAsync(
                new NewBillingCustomer(reference, email, firstName, FallbackLastName), ct);
            await _claims.CompleteCustomerAsync(userId, customer.Id, CancellationToken.None);
            _logger.LogInformation("Billing customer {CustomerId} created for user {User}", customer.Id, ShortId(userId));
            return customer.Id;
        }
        catch (Exception ex) when (ex is BillingProviderRejectedException
                                   || ex is BillingProviderException { Kind: BillingFailureKind.ConfigFault or BillingFailureKind.Unavailable })
        {
            // Definitely nothing was created: free the claim. Unknown outcomes keep it (stale-claim takeover settles later).
            await _claims.ReleaseCustomerAsync(userId, claimResult.Claim.ClaimedAtUtc, CancellationToken.None);
            throw;
        }
    }

    private static string ResolveEmail(BillingUser user)
    {
        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email!.Trim() : user.UserName.Trim();
        if (email.IndexOf('@') < 1)
        {
            throw new BillingRequestException("A valid e-mail address is required on the account to subscribe.");
        }
        return email;
    }

    /// <summary>Short, non-reversible id for logs (never the user name or e-mail).</summary>
    public static string ShortId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    private async Task<T> Bounded<T>(TimeSpan budget, CancellationToken callerToken, Func<CancellationToken, Task<T>> work)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(budget);
        try
        {
            return await work(cts.Token);
        }
        catch (OperationCanceledException ex) when (!callerToken.IsCancellationRequested)
        {
            _logger.LogWarning("Billing call exceeded its {Seconds}s budget", (int)budget.TotalSeconds);
            throw new BillingProviderException(BillingFailureKind.Timeout, "The billing provider did not answer in time.", ex);
        }
    }
}
