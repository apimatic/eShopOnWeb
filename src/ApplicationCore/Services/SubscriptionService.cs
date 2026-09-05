using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionService : ISubscriptionService
{
    // States in which a subscription is still "in force" from the buyer's point of view -
    // i.e. re-subscribing to the same plan should not create a second one.
    // See https://maxio.zendesk.com/hc/en-us/articles/24252119027853-Subscription-States
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "trialing", "assessing", "active", "soft_failure", "past_due", "unpaid", "paused", "on_hold", "suspended", "awaiting_signup"
    };

    private readonly IMaxioService _maxioService;

    public SubscriptionService(IMaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    public Task<IReadOnlyList<MaxioProduct>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
        => _maxioService.ListPlansAsync(cancellationToken);

    public async Task<MaxioSubscription> SubscribeAsync(string buyerReference, string buyerEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerReference, nameof(buyerReference));
        Guard.Against.NullOrEmpty(buyerEmail, nameof(buyerEmail));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        var customer = await EnsureCustomerAsync(buyerReference, buyerEmail, cancellationToken);

        var existingSubscriptions = await _maxioService.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            s.Product is not null &&
            string.Equals(s.Product.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            LiveSubscriptionStates.Contains(s.State));

        if (existing is not null)
        {
            return existing;
        }

        return await _maxioService.CreateSubscriptionAsync(buyerReference, planHandle, cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForBuyerAsync(string buyerReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerReference, nameof(buyerReference));

        var customer = await _maxioService.FindCustomerByReferenceAsync(buyerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _maxioService.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string buyerReference, string buyerEmail, CancellationToken cancellationToken)
    {
        var existing = await _maxioService.FindCustomerByReferenceAsync(buyerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(buyerEmail);
        var newCustomer = new NewMaxioCustomer(firstName, lastName, buyerEmail, buyerReference);

        try
        {
            return await _maxioService.CreateCustomerAsync(newCustomer, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Reference must be unique in Maxio - a concurrent request (e.g. a double-click)
            // may have created the customer between our lookup and this create call.
            var raceWinner = await _maxioService.FindCustomerByReferenceAsync(buyerReference, cancellationToken);
            if (raceWinner is not null)
            {
                return raceWinner;
            }

            throw;
        }
    }

    private static (string FirstName, string LastName) SplitDisplayName(string email)
    {
        var localPart = email.Split('@')[0];
        return (localPart, "eShopOnWeb Customer");
    }
}
