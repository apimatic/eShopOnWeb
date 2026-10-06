using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscribe flow against the billing provider. Every public operation runs under one
/// request budget, and every subscribe is serialized per user by a claim in the local store taken
/// <em>before</em> anything is written to the provider.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private const string InProgressMessage =
        "A subscription request for this account is already in progress. Try again shortly.";

    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionEnrollmentStore _store;
    private readonly SubscriptionServiceSettings _settings;
    private readonly TimeProvider _clock;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(ISubscriptionBillingGateway gateway,
        ISubscriptionEnrollmentStore store,
        SubscriptionServiceSettings settings,
        TimeProvider clock,
        IAppLogger<SubscriptionService> logger)
    {
        _gateway = gateway;
        _store = store;
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    public Task<BillingPlanCatalog> GetPlansAsync(CancellationToken cancellationToken) =>
        WithinBudgetAsync(token => _gateway.GetPlansAsync(token), cancellationToken);

    public Task<IReadOnlyList<BillingSubscription>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        return WithinBudgetAsync(async token =>
        {
            var customer = await _gateway.FindCustomerByReferenceAsync(CustomerReference(userName), token);
            if (customer is null)
            {
                return (IReadOnlyList<BillingSubscription>)Array.Empty<BillingSubscription>();
            }
            return await _gateway.ListCustomerSubscriptionsAsync(customer.Id, token);
        }, cancellationToken);
    }

    public Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        Guard.Against.Null(command, nameof(command));
        Guard.Against.NullOrWhiteSpace(command.UserName, nameof(command.UserName));
        Guard.Against.NullOrWhiteSpace(command.Email, nameof(command.Email));
        Guard.Against.NullOrWhiteSpace(command.PlanHandle, nameof(command.PlanHandle));
        return WithinBudgetAsync(token => SubscribeWithinBudgetAsync(command, token), cancellationToken);
    }

    private async Task<SubscribeResult> SubscribeWithinBudgetAsync(SubscribeCommand command, CancellationToken token)
    {
        // Invariant: only a plan the configured family currently offers may be sent to CreateSubscription.
        var plan = await ResolvePlanAsync(command.PlanHandle, token);

        var enrollment = await _store.FindAsync(command.UserName, token);
        if (enrollment is null)
        {
            var claim = SubscriptionEnrollment.Claim(command.UserName, plan.Handle,
                SubscriptionReference(command.UserName), _clock.GetUtcNow());
            if (await _store.TryClaimAsync(claim, token))
            {
                return await EnrollAsync(claim, plan, command, token);
            }

            // Another request claimed this user first; continue from whatever it recorded.
            enrollment = await _store.FindAsync(command.UserName, token)
                ?? throw new SubscriptionConflictException(InProgressMessage);
        }

        return await ResumeAsync(enrollment, plan, command, token);
    }

    private async Task<SubscribeResult> ResumeAsync(SubscriptionEnrollment enrollment, BillingPlan plan,
        SubscribeCommand command, CancellationToken token)
    {
        var now = _clock.GetUtcNow();
        if (enrollment.Status == EnrollmentStatus.Pending && !enrollment.IsStale(now, _settings.StaleClaimAfter))
        {
            throw new SubscriptionConflictException(InProgressMessage);
        }

        // Enrolled, unknown outcome, or an abandoned claim: the provider holds the truth — read it by reference.
        var current = await _gateway.FindSubscriptionByReferenceAsync(enrollment.SubscriptionReference, token);
        if (current is not null)
        {
            EnsureBelongsToEnrollment(current, enrollment);
            if (enrollment.Status != EnrollmentStatus.Enrolled)
            {
                enrollment.MarkActive(current.CustomerId, current.Id, current.PlanHandle ?? enrollment.PlanHandle, now);
                await _store.TrySaveAsync(enrollment, token);
                _logger.LogInformation("Settled subscription {SubscriptionId} for {UserName} from an earlier request.",
                    current.Id, command.UserName);
            }
            return ExistingOrConflict(current, enrollment, plan);
        }

        // The provider has no subscription for this user: take the claim over and enroll.
        enrollment.Restart(plan.Handle, now);
        if (!await _store.TrySaveAsync(enrollment, token))
        {
            throw new SubscriptionConflictException(InProgressMessage);
        }
        return await EnrollAsync(enrollment, plan, command, token);
    }

    /// <summary>Runs while holding the user's claim (status Pending).</summary>
    private async Task<SubscribeResult> EnrollAsync(SubscriptionEnrollment enrollment, BillingPlan plan,
        SubscribeCommand command, CancellationToken token)
    {
        try
        {
            var customer = await EnsureCustomerAsync(command, token);
            enrollment.AttachCustomer(customer.Id, _clock.GetUtcNow());
            await SaveHeldClaimAsync(enrollment, token);

            // Reconcile first: a subscription with our reference may already exist (e.g. the local store was
            // reset). Never rely on the provider rejecting a duplicate reference.
            var existing = await _gateway.FindSubscriptionByReferenceAsync(enrollment.SubscriptionReference, token);
            if (existing is not null)
            {
                EnsureBelongsToEnrollment(existing, enrollment);
                enrollment.MarkActive(customer.Id, existing.Id, existing.PlanHandle ?? plan.Handle, _clock.GetUtcNow());
                await SaveHeldClaimAsync(enrollment, token);
                return ExistingOrConflict(existing, enrollment, plan);
            }

            BillingSubscription created;
            try
            {
                created = await _gateway.CreateSubscriptionAsync(customer.Id, plan.Handle,
                    enrollment.SubscriptionReference, token);
            }
            catch (BillingOutcomeUnknownException ex)
            {
                return await SettleUnknownSubscriptionAsync(enrollment, customer.Id, plan, ex);
            }

            enrollment.MarkActive(customer.Id, created.Id, created.PlanHandle ?? plan.Handle, _clock.GetUtcNow());
            await SaveHeldClaimAsync(enrollment, CancellationToken.None);
            _logger.LogInformation("Created subscription {SubscriptionId} on plan {PlanHandle} for {UserName}.",
                created.Id, plan.Handle, command.UserName);
            return new SubscribeResult(created, Created: true);
        }
        catch (Exception ex) when (enrollment.Status == EnrollmentStatus.Pending
                                   && ex is BillingProviderException or OperationCanceledException or SubscriptionConflictException)
        {
            // Nothing was enrolled at the provider: release the claim so the shopper can try again.
            await _store.ReleaseAsync(enrollment, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// The CreateSubscription call went unanswered — it may have landed. Look it up by the reference we sent,
    /// within its own small budget; if that cannot settle it, record the outcome as unknown so the next
    /// request settles it (see <see cref="ResumeAsync"/>).
    /// </summary>
    private async Task<SubscribeResult> SettleUnknownSubscriptionAsync(SubscriptionEnrollment enrollment,
        int customerId, BillingPlan plan, BillingOutcomeUnknownException failure)
    {
        BillingSubscription? found = null;
        using (var settle = new CancellationTokenSource(_settings.SettleBudget, _clock))
        {
            try
            {
                found = await _gateway.FindSubscriptionByReferenceAsync(enrollment.SubscriptionReference, settle.Token);
            }
            catch (Exception ex) when (ex is BillingProviderException or OperationCanceledException)
            {
                _logger.LogWarning("Could not settle subscription {Reference} after an unanswered create: {Reason}",
                    enrollment.SubscriptionReference, ex.Message);
            }
        }

        if (found is not null)
        {
            enrollment.MarkActive(customerId, found.Id, found.PlanHandle ?? plan.Handle, _clock.GetUtcNow());
            await _store.TrySaveAsync(enrollment, CancellationToken.None);
            _logger.LogInformation("Subscription {SubscriptionId} confirmed by reference after an unanswered create.", found.Id);
            return new SubscribeResult(found, Created: true);
        }

        enrollment.MarkOutcomeUnknown(_clock.GetUtcNow());
        await _store.TrySaveAsync(enrollment, CancellationToken.None);
        _logger.LogWarning("Subscription {Reference} outcome unknown; it will be settled on the next request.",
            enrollment.SubscriptionReference);
        throw new SubscriptionOutcomeUnknownException(
            "Maxio did not respond in time, so the subscription could not be confirmed. " +
            "It is being verified - repeat the request to see the result.", failure);
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(SubscribeCommand command, CancellationToken token)
    {
        var reference = CustomerReference(command.UserName);
        var customer = await _gateway.FindCustomerByReferenceAsync(reference, token);
        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = NamesFromEmail(command.Email);
        customer = await _gateway.CreateCustomerAsync(
            new BillingCustomerProfile(reference, command.Email, firstName, lastName), token);
        _logger.LogInformation("Created billing customer {CustomerId} for {UserName}.", customer.Id, command.UserName);
        return customer;
    }

    private async Task<BillingPlan> ResolvePlanAsync(string planHandle, CancellationToken token)
    {
        var catalog = await _gateway.GetPlansAsync(token);
        return catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new UnknownSubscriptionPlanException(planHandle);
    }

    private async Task SaveHeldClaimAsync(SubscriptionEnrollment enrollment, CancellationToken token)
    {
        if (!await _store.TrySaveAsync(enrollment, token))
        {
            // Only possible if another request decided this claim was abandoned and took it over.
            _logger.LogWarning("Subscription claim for {UserName} was taken over by another request.", enrollment.UserName);
        }
    }

    private static SubscribeResult ExistingOrConflict(BillingSubscription subscription, SubscriptionEnrollment enrollment, BillingPlan requested)
    {
        var heldPlan = subscription.PlanHandle ?? enrollment.PlanHandle;
        if (!string.Equals(heldPlan, requested.Handle, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionConflictException(
                $"This account is already subscribed to plan '{heldPlan}'. Changing plans is not supported here.");
        }
        return new SubscribeResult(subscription, Created: false);
    }

    private static void EnsureBelongsToEnrollment(BillingSubscription subscription, SubscriptionEnrollment enrollment)
    {
        if (enrollment.BillingCustomerId is int expected && subscription.CustomerId is int actual && expected != actual)
        {
            throw new BillingProviderException(BillingFailureKind.Misconfigured,
                $"Subscription reference '{enrollment.SubscriptionReference}' belongs to a different billing customer.");
        }
    }

    private async Task<T> WithinBudgetAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(_settings.RequestBudget, _clock);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            return await work(budget.Token);
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException(BillingFailureKind.Timeout,
                $"Maxio did not respond within {_settings.RequestBudget.TotalSeconds:0} seconds.", innerException: ex);
        }
    }

    private string CustomerReference(string userName) => $"{_settings.ReferencePrefix}-{userName.ToLowerInvariant()}";

    private string SubscriptionReference(string userName) => $"{_settings.ReferencePrefix}-sub-{userName.ToLowerInvariant()}";

    private static (string FirstName, string LastName) NamesFromEmail(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        return (local, "eShop Customer");
    }
}
