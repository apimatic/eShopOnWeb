using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscription use cases over the billing seam, mirroring the role
/// <see cref="OrderService"/> plays for the one-time purchase flow (plan.md §4.2).
/// </summary>
/// <remarks>
/// Two rules shape this class:
/// <list type="bullet">
/// <item>Everything that can be decided locally is decided before the provider is called — an illegal
/// transition, a no-op plan change, or a non-positive usage quantity never leaves eShopOnWeb.</item>
/// <item>Notification publishing is best-effort. A handler that throws is logged and swallowed, because a
/// successful provider call must never be undone by a failed in-process side effect (plan.md §2.5).</item>
/// </list>
/// </remarks>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly ISubscriptionSettings _settings;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IBillingClient billingClient, ISubscriptionSettings settings,
        IPublisher publisher, IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient ?? throw new ArgumentNullException(nameof(billingClient));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ---------------------------------------------------------------------------------------------
    // UC1 — browse plans, subscribe, review
    // ---------------------------------------------------------------------------------------------

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _billingClient.ListPlansAsync(cancellationToken);

    public async Task<Subscription> SubscribeAsync(string userReference, string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        var reference = userReference.Trim();
        var handle = planHandle.Trim();

        var plan = await _billingClient.FindPlanByHandleAsync(handle, cancellationToken).ConfigureAwait(false)
            ?? throw new BillingConfigurationException(
                $"Plan '{handle}' is not available. The billing catalog may have been re-seeded — check the " +
                "configured product handles (plan.md UC0).");

        if (plan.Archived)
        {
            throw new BillingConfigurationException($"Plan '{handle}' is archived and cannot be subscribed to.");
        }

        var customer = await EnsureCustomerAsync(reference, cancellationToken).ConfigureAwait(false);

        // A repeated subscribe — a double-click, or a retry after a partial failure — must never create a
        // second enrollment. The provider-side customer reference is what makes this detectable.
        var existing = await _billingClient
            .ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken)
            .ConfigureAwait(false);

        var live = existing.FirstOrDefault(subscription => IsLive(subscription.State));
        if (live is not null)
        {
            _logger.LogInformation("{0} already has live subscription {1}; returning it instead of enrolling again.",
                reference, live.Id);
            return live;
        }

        var created = await _billingClient
            .CreateSubscriptionAsync(customer, handle, cancellationToken)
            .ConfigureAwait(false);

        await PublishAsync(
            new SubscriptionActivated(reference, created.Id, created.PlanHandle, created.PlanPrice, created.ProviderState),
            cancellationToken).ConfigureAwait(false);

        return created;
    }

    public async Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(string userReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var customer = await _billingClient
            .FindCustomerByReferenceAsync(userReference.Trim(), cancellationToken)
            .ConfigureAwait(false);

        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        return await _billingClient
            .ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Subscription?> FindActiveSubscriptionAsync(string userReference,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await ListSubscriptionsAsync(userReference, cancellationToken).ConfigureAwait(false);
        return subscriptions.FirstOrDefault(subscription => IsLive(subscription.State));
    }

    // ---------------------------------------------------------------------------------------------
    // UC2 — pay-as-you-go usage
    // ---------------------------------------------------------------------------------------------

    public async Task<UsageSummary> RecordUsageAsync(int subscriptionId, int quantity, string? memo,
        string? restrictToUserReference, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            throw new InvalidSubscriptionOperationException(
                $"Usage quantity must be greater than zero; {quantity} was reported.");
        }

        var subscription = await RequireSubscriptionAsync(subscriptionId, restrictToUserReference, cancellationToken)
            .ConfigureAwait(false);

        if (!subscription.CanRecordUsage)
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscriptionId} is '{subscription.ProviderState}'; usage can only be recorded " +
                "against a live subscription.");
        }

        var component = await RequireMeteredComponentAsync(cancellationToken).ConfigureAwait(false);

        var record = await _billingClient
            .RecordUsageAsync(subscriptionId, component.Handle, quantity, memo, cancellationToken)
            .ConfigureAwait(false);

        var total = await ReadPeriodToDateAsync(subscriptionId, component.Handle, cancellationToken)
            .ConfigureAwait(false);

        return new UsageSummary(component.Handle, record, total, component.UnitPrice);
    }

    public async Task<UsageSummary?> RecordUsageForUserAsync(string userReference, int quantity, string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var subscription = await FindActiveSubscriptionAsync(userReference, cancellationToken).ConfigureAwait(false);

        if (subscription is null)
        {
            _logger.LogInformation("{0} has no live subscription; {1} unit(s) of usage were not recorded.",
                userReference, quantity);
            return null;
        }

        return await RecordUsageAsync(subscription.Id, quantity, memo, userReference, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<UsageSummary?> GetUsageSummaryAsync(int subscriptionId, string? restrictToUserReference,
        CancellationToken cancellationToken = default)
    {
        var subscription = await RequireSubscriptionAsync(subscriptionId, restrictToUserReference, cancellationToken)
            .ConfigureAwait(false);

        var component = await _billingClient
            .FindComponentByHandleAsync(_settings.MeteredComponentHandle, cancellationToken)
            .ConfigureAwait(false);

        if (component is null || !component.IsMetered)
        {
            return null;
        }

        var total = await ReadPeriodToDateAsync(subscription.Id, component.Handle, cancellationToken)
            .ConfigureAwait(false);

        return new UsageSummary(component.Handle, record: null, periodToDateQuantity: total, unitPrice: component.UnitPrice);
    }

    // ---------------------------------------------------------------------------------------------
    // UC3 — plan change
    // ---------------------------------------------------------------------------------------------

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, string? restrictToUserReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await RequireSubscriptionAsync(subscriptionId, restrictToUserReference, cancellationToken)
            .ConfigureAwait(false);

        await EnsurePlanChangeIsLegalAsync(subscription, targetPlanHandle.Trim(), cancellationToken)
            .ConfigureAwait(false);

        return await _billingClient
            .PreviewPlanChangeAsync(subscriptionId, targetPlanHandle.Trim(), timing, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing,
        decimal previewedNetAmount, string? restrictToUserReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var handle = targetPlanHandle.Trim();
        var subscription = await RequireSubscriptionAsync(subscriptionId, restrictToUserReference, cancellationToken)
            .ConfigureAwait(false);

        await EnsurePlanChangeIsLegalAsync(subscription, handle, cancellationToken).ConfigureAwait(false);

        // The customer confirmed a specific amount. Re-price immediately before committing so a proration
        // basis that moved in between is rejected rather than silently charged (plan.md UC3).
        var fresh = await _billingClient
            .PreviewPlanChangeAsync(subscriptionId, handle, timing, cancellationToken)
            .ConfigureAwait(false);

        if (fresh.NetAmount != previewedNetAmount)
        {
            throw new InvalidSubscriptionOperationException(
                $"The prorated amount changed from {BillingMoney.ToDisplay(previewedNetAmount)} to " +
                $"{BillingMoney.ToDisplay(fresh.NetAmount)} since the preview " +
                "was shown. Review the new amount and confirm again.");
        }

        var updated = await _billingClient
            .ChangePlanAsync(subscriptionId, handle, timing, cancellationToken)
            .ConfigureAwait(false);

        await PublishAsync(
            new SubscriptionPlanChanged(
                subscription.CustomerReference,
                subscriptionId,
                subscription.PlanHandle,
                handle,
                fresh.NetAmount,
                timing == PlanChangeTiming.Immediately),
            cancellationToken).ConfigureAwait(false);

        return updated;
    }

    // ---------------------------------------------------------------------------------------------
    // UC4 — lifecycle
    // ---------------------------------------------------------------------------------------------

    public async Task<Subscription> ApplyLifecycleActionAsync(int subscriptionId, SubscriptionLifecycleAction action,
        string? reason, string? restrictToUserReference, CancellationToken cancellationToken = default)
    {
        var subscription = await RequireSubscriptionAsync(subscriptionId, restrictToUserReference, cancellationToken)
            .ConfigureAwait(false);

        if (!subscription.Allows(action))
        {
            var allowed = subscription.AllowedActions.Count == 0
                ? "none"
                : string.Join(", ", subscription.AllowedActions);

            throw new InvalidSubscriptionOperationException(
                $"'{action}' is not allowed while subscription {subscriptionId} is '{subscription.ProviderState}'. " +
                $"Allowed from here: {allowed}.");
        }

        var updated = await _billingClient
            .ApplyLifecycleActionAsync(subscriptionId, action, reason, cancellationToken)
            .ConfigureAwait(false);

        await PublishAsync(
            new SubscriptionStateChanged(
                subscription.CustomerReference,
                subscriptionId,
                subscription.State,
                updated.State,
                action,
                EffectiveDateFor(action, updated),
                reason),
            cancellationToken).ConfigureAwait(false);

        return updated;
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private static bool IsLive(SubscriptionState state) =>
        state is SubscriptionState.Active or SubscriptionState.Trialing or SubscriptionState.Paused
            or SubscriptionState.PastDue;

    private static DateTimeOffset? EffectiveDateFor(SubscriptionLifecycleAction action, Subscription subscription) =>
        action == SubscriptionLifecycleAction.CancelAtEndOfPeriod
            ? subscription.CancellationScheduledAt ?? subscription.CurrentPeriodEnd
            : DateTimeOffset.UtcNow;

    private async Task<BillingCustomer> EnsureCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var existing = await _billingClient.FindCustomerByReferenceAsync(reference, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(reference);

        return await _billingClient
            .CreateCustomerAsync(new BillingCustomerRegistration(reference, reference, firstName, lastName), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// eShopOnWeb's identity carries only an email, but the provider wants a first and last name. The email's
    /// local part is split deterministically so the same user always produces the same customer record.
    /// </summary>
    internal static (string FirstName, string LastName) SplitName(string userReference)
    {
        var localPart = userReference.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var first = parts.Length > 0 ? Capitalise(parts[0]) : "eShop";
        var last = parts.Length > 1 ? Capitalise(parts[^1]) : "Customer";

        return (first, last);
    }

    private static string Capitalise(string value) =>
        value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value.Substring(1);

    private async Task<Subscription> RequireSubscriptionAsync(int subscriptionId, string? restrictToUserReference,
        CancellationToken cancellationToken)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            .ConfigureAwait(false);

        // A subscription belonging to somebody else is reported exactly like one that does not exist, so a
        // signed-in customer cannot probe for other people's subscription ids.
        if (subscription is null ||
            (restrictToUserReference is not null &&
             !string.Equals(subscription.CustomerReference, restrictToUserReference.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidSubscriptionOperationException($"No subscription with id {subscriptionId} was found.");
        }

        return subscription;
    }

    private async Task<MeteredComponent> RequireMeteredComponentAsync(CancellationToken cancellationToken)
    {
        var handle = _settings.MeteredComponentHandle;

        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingConfigurationException("No metered component handle is configured for usage billing.");
        }

        var component = await _billingClient.FindComponentByHandleAsync(handle, cancellationToken).ConfigureAwait(false)
            ?? throw new BillingConfigurationException(
                $"Metered component '{handle}' does not exist on the configured product family. " +
                "Seed the sandbox before recording usage (plan.md UC0).");

        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"Component '{handle}' is of kind '{component.Kind}', not metered. It cannot be converted in " +
                "place — archive it and recreate it as metered (plan.md UC0).");
        }

        return component;
    }

    /// <summary>
    /// Reads the running total. A failure here must not fail an operation whose usage was already accepted,
    /// so the total is simply reported as unavailable (plan.md UC2).
    /// </summary>
    private async Task<int?> ReadPeriodToDateAsync(int subscriptionId, string componentHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _billingClient
                .GetPeriodToDateUsageAsync(subscriptionId, componentHandle, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Period-to-date usage for subscription {0} could not be read: {1}",
                subscriptionId, ex.Message);
            return null;
        }
    }

    private async Task EnsurePlanChangeIsLegalAsync(Subscription subscription, string targetPlanHandle,
        CancellationToken cancellationToken)
    {
        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscription.Id} is already on plan '{targetPlanHandle}'.");
        }

        if (!subscription.CanChangePlan)
        {
            throw new InvalidSubscriptionOperationException(
                $"A plan change is not allowed while subscription {subscription.Id} is " +
                $"'{subscription.ProviderState}'. Reactivate it first.");
        }

        var target = await _billingClient.FindPlanByHandleAsync(targetPlanHandle, cancellationToken)
            .ConfigureAwait(false);

        if (target is null || target.Archived)
        {
            throw new BillingConfigurationException(
                $"Target plan '{targetPlanHandle}' does not resolve or is archived (plan.md UC0).");
        }
    }

    /// <summary>
    /// Publishes best-effort. eShopOnWeb has no broker and no outbox, so a handler failure is logged and the
    /// completed provider operation stands (plan.md §2.5).
    /// </summary>
    private async Task PublishAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("In-process notification {0} failed after the billing operation succeeded: {1}",
                notification.GetType().Name, ex.Message);
        }
    }
}
