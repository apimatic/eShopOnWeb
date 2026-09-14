using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioBillingService : IMaxioBillingService
{
    // Subscription states that represent a subscription that is no longer "in the way" of a
    // fresh signup to the same plan. Every other state (active, trialing, past_due, on_hold,
    // etc. - see maxio-spec/components/schemas/Subscription-State.yaml) is treated as "already
    // subscribed" so re-subscribing is a safe no-op.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    // Maxio has no idempotency-key mechanism for subscription creation (see maxio-spec), so a
    // per-user in-process lock closes the double-click race between the "does a subscription
    // already exist" check and the "create one" call within a single server instance.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly IMaxioApiClient _client;
    private readonly MaxioOptions _options;

    public MaxioBillingService(IMaxioApiClient client, IOptions<MaxioOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsAsync(cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .Where(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .Select(MapPlan)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var plans = await GetAvailablePlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new PlanNotFoundException(request.PlanHandle);
        }

        var gate = SubscribeLocks.GetOrAdd(request.UserReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(request, cancellationToken);

            var existingSubscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var blockingSubscription = existingSubscriptions.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase) &&
                !TerminalStates.Contains(s.State));

            if (blockingSubscription is not null)
            {
                return MapSubscription(blockingSubscription);
            }

            var created = await _client.CreateSubscriptionAsync(new CreateSubscriptionAttributes
            {
                ProductHandle = request.PlanHandle,
                CustomerId = customer.Id,
                // Plans that don't require a stored payment method (maxio-spec Create-Subscription's
                // own "Basic" example pairs no card with payment_collection_method=remittance) still
                // default to automatic/card-based collection otherwise, and 422 with "no payment
                // method on file" since this flow never collects card details.
                PaymentCollectionMethod = plan.RequiresPaymentMethod ? null : "remittance"
            }, cancellationToken);

            return MapSubscription(created);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).OrderByDescending(s => s.CreatedAt).ToList();
    }

    private async Task<Customer> EnsureCustomerAsync(SubscribeRequest request, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(request.UserReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _client.CreateCustomerAsync(new CreateCustomerAttributes
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Reference = request.UserReference
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Reference collisions (concurrent request, or a customer created out-of-band with
            // the same reference) fail creation with a 422. Since "reference" is unique per
            // Maxio's own contract, re-look it up rather than surfacing a spurious error.
            var raceWinner = await _client.FindCustomerByReferenceAsync(request.UserReference, cancellationToken);
            if (raceWinner is not null)
            {
                return raceWinner;
            }

            throw;
        }
    }

    private static SubscriptionPlanDto MapPlan(Product product) => new()
    {
        ProductId = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static SubscriptionDto MapSubscription(Subscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt
    };
}
