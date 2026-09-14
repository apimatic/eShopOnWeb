using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Subscription states Maxio considers permanently over; anything else (active, trialing,
    // past_due, unpaid, awaiting_signup, suspended, ...) still represents a live enrollment
    // that a repeat "subscribe" call should return rather than duplicate.
    private static readonly HashSet<string> TerminalStates = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired"
    };

    private readonly IMaxioApiClient _client;
    private readonly IUserOperationLock _userLock;

    public MaxioSubscriptionService(IMaxioApiClient client, IUserOperationLock userLock)
    {
        _client = client;
        _userLock = userLock;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(cancellationToken);
        return products.Select(MapPlan).ToList();
    }

    public async Task<SubscriptionSummary> SubscribeAsync(string userId, string userEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        using (await _userLock.AcquireAsync(userId, cancellationToken))
        {
            var plan = await _client.GetProductByHandleAsync(planHandle, cancellationToken);
            if (plan is null)
            {
                throw new MaxioPlanNotFoundException(planHandle);
            }

            var customer = await FindOrCreateCustomerAsync(userId, userEmail, cancellationToken);

            var existingSubscriptions = await _client.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
            var liveSubscription = existingSubscriptions.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, planHandle, System.StringComparison.OrdinalIgnoreCase) &&
                !TerminalStates.Contains(s.State));

            if (liveSubscription is not null)
            {
                return MapSubscription(liveSubscription);
            }

            var created = await _client.CreateSubscriptionAsync(userId, planHandle, cancellationToken);
            return MapSubscription(created);
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return System.Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await _client.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<MaxioCustomerWire> FindOrCreateCustomerAsync(string userId, string userEmail, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _client.CreateCustomerAsync(userId, userEmail, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.IsClientError)
        {
            // Another concurrent request (e.g. a different process) may have created the
            // customer between our lookup and our create attempt (reference must be unique).
            var raceWinner = await _client.FindCustomerByReferenceAsync(userId, cancellationToken);
            if (raceWinner is not null)
            {
                return raceWinner;
            }
            throw;
        }
    }

    private static SubscriptionPlan MapPlan(MaxioProductWire product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresCreditCard = product.RequireCreditCard
    };

    private static SubscriptionSummary MapSubscription(MaxioSubscriptionWire subscription) => new()
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
