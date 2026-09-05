using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    // Subscription states that mean "this plan enrollment is over" - a fresh Subscribe call
    // for the same plan should create a new subscription rather than returning these.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _options;

    // Maxio has no idempotency-key mechanism (see maxio-spec), so we close the common
    // same-process double-click race by serializing Subscribe calls per customer reference.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeLocks = new();

    public SubscriptionBillingService(IMaxioClient maxioClient, MaxioOptions options)
    {
        _maxioClient = maxioClient;
        _options = options;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsAsync(cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .Where(p => string.Equals(p.ProductFamilyHandle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.PriceInCents)
            .Select(p => new SubscriptionPlan(p.Handle, p.Name, p.Description, p.PriceInCents, p.Interval, p.IntervalUnit))
            .ToList();
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken = default)
    {
        var plans = await GetAvailablePlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(request.PlanHandle);
        }

        var gate = _subscribeLocks.GetOrAdd(request.CustomerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(request, cancellationToken);

            var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = existingSubscriptions.FirstOrDefault(s =>
                string.Equals(s.ProductHandle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                !TerminalStates.Contains(s.State));

            if (existing is not null)
            {
                return ToEnrollment(existing, alreadyExisted: true);
            }

            var created = await _maxioClient.CreateSubscriptionAsync(
                new MaxioCreateSubscription(customer.Id, plan.Handle),
                cancellationToken);

            return ToEnrollment(created, alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionEnrollment>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionEnrollment>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(s => ToEnrollment(s, alreadyExisted: true)).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(request.CustomerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _maxioClient.CreateCustomerAsync(
                new MaxioCreateCustomer(request.FirstName, request.LastName, request.Email, request.CustomerReference),
                cancellationToken);
        }
        catch (MaxioApiException)
        {
            // Maxio enforces reference uniqueness. A failure here most likely means a
            // concurrent request (e.g. from another process) already created the customer.
            var concurrentlyCreated = await _maxioClient.FindCustomerByReferenceAsync(request.CustomerReference, cancellationToken);
            if (concurrentlyCreated is not null)
            {
                return concurrentlyCreated;
            }

            throw;
        }
    }

    private static SubscriptionEnrollment ToEnrollment(MaxioSubscription subscription, bool alreadyExisted) => new(
        subscription.Id,
        subscription.ProductHandle,
        subscription.ProductName,
        subscription.ProductPriceInCents,
        subscription.State,
        subscription.CreatedAt,
        subscription.CurrentPeriodEndsAt,
        subscription.NextAssessmentAt,
        alreadyExisted);
}
