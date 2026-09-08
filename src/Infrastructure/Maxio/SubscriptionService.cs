using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates recurring-subscription billing with Maxio Advanced Billing as the system of
/// record. The eShopOnWeb user id is used as the unique customer reference in Maxio, so a
/// billing customer is created at most once per user, and subscription lookups are always
/// resolved against Maxio (no local billing state to keep in sync).
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioGateway _gateway;
    private readonly MaxioOptions _options;
    private readonly PerUserOperationLocks _perUserLocks;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioGateway gateway,
        IOptions<MaxioOptions> options,
        PerUserOperationLocks perUserLocks,
        IAppLogger<SubscriptionService> logger)
    {
        _gateway = gateway;
        _options = options.Value;
        _perUserLocks = perUserLocks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanSummary>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _gateway.ListFamilyProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt == null)
            .OrderBy(p => p.PriceInCents)
            .Select(p => new SubscriptionPlanSummary(
                Handle: p.Handle,
                Name: p.Name,
                Description: p.Description,
                PriceInCents: p.PriceInCents,
                BillingInterval: p.Interval,
                BillingIntervalUnit: p.IntervalUnit,
                RequiresPaymentMethod: p.RequireCreditCard))
            .ToList();
    }

    public async Task<SubscriptionResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var plan = await ResolvePlanAsync(command.PlanHandle, cancellationToken);

        using (await _perUserLocks.AcquireAsync(command.UserReference, cancellationToken))
        {
            try
            {
                var customer = await EnsureCustomerAsync(command, cancellationToken);

                var existingSubscriptions = await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
                var liveSubscription = existingSubscriptions.FirstOrDefault(s =>
                    !IsTerminalState(s.State) && string.Equals(s.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase));
                if (liveSubscription != null)
                {
                    _logger.LogInformation("User {UserReference} is already subscribed to plan {PlanHandle} (subscription {SubscriptionId}).",
                        command.UserReference, plan.Handle, liveSubscription.Id);
                    return new SubscriptionResult(MapSubscription(liveSubscription), AlreadySubscribed: true);
                }

                var created = await _gateway.CreateSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
                _logger.LogInformation("User {UserReference} subscribed to plan {PlanHandle} (subscription {SubscriptionId}).",
                    command.UserReference, plan.Handle, created.Id);
                return new SubscriptionResult(MapSubscription(created), AlreadySubscribed: false);
            }
            catch (MaxioApiException ex)
            {
                _logger.LogError("Maxio API call failed while subscribing user {UserReference} to plan {PlanHandle}. Status: {StatusCode}. Error: {Error}",
                    command.UserReference, plan.Handle, ex.StatusCode?.ToString() ?? "unknown", ex.Message);
                throw new BillingProviderException(
                    $"The billing system could not process the subscription to plan '{plan.Handle}'.", ex);
            }
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetMySubscriptionsAsync(string userReference, CancellationToken cancellationToken)
    {
        try
        {
            var customer = await _gateway.FindCustomerByReferenceAsync(userReference, cancellationToken);
            if (customer == null)
            {
                return Array.Empty<SubscriptionDetails>();
            }

            var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            return subscriptions
                .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
                .Select(MapSubscription)
                .ToList();
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError("Maxio API call failed while listing subscriptions for user {UserReference}. Status: {StatusCode}. Error: {Error}",
                userReference, ex.StatusCode?.ToString() ?? "unknown", ex.Message);
            throw new BillingProviderException("The billing system could not list the user's subscriptions.", ex);
        }
    }

    private async Task<MaxioProduct> ResolvePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var products = await _gateway.ListFamilyProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        var plan = products.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan == null || plan.ArchivedAt != null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }
        return plan;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var existing = await _gateway.FindCustomerByReferenceAsync(command.UserReference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        try
        {
            var created = await _gateway.CreateCustomerAsync(
                email: command.Email,
                firstName: command.FirstName,
                lastName: command.LastName,
                reference: command.UserReference,
                cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserReference}.", created.Id, command.UserReference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // A concurrent signup already claimed this unique reference; the customer must now exist.
            var raced = await _gateway.FindCustomerByReferenceAsync(command.UserReference, cancellationToken);
            if (raced != null)
            {
                return raced;
            }
            throw new BillingProviderException(
                "The billing system rejected creation of the customer for this user.", ex);
        }
    }

    private static bool IsTerminalState(string? state) =>
        state is "canceled" or "expired";

    private static SubscriptionDetails MapSubscription(MaxioSubscription subscription) =>
        new(
            Id: subscription.Id,
            State: subscription.State,
            PlanHandle: subscription.PlanHandle ?? string.Empty,
            PlanName: subscription.PlanName ?? string.Empty,
            PriceInCents: subscription.ProductPriceInCents,
            CreatedAt: subscription.CreatedAt,
            ActivatedAt: subscription.ActivatedAt,
            CurrentPeriodStart: subscription.CurrentPeriodStartedAt,
            NextBillingAt: subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            CanceledAt: subscription.CanceledAt);
}
