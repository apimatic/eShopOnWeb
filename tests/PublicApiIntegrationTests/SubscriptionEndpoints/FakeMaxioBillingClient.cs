using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// In-memory stand-in for the Maxio Billing API used by the subscription endpoint tests,
/// so the functional tests never call the real (sandbox) provider.
/// </summary>
public class FakeMaxioBillingClient : IMaxioBillingClient
{
    public List<MaxioPlan> Plans { get; } = new()
    {
        new MaxioPlan { Id = 1, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month", ProductFamilyHandle = "eshop-subscribe" },
        new MaxioPlan { Id = 2, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month", ProductFamilyHandle = "eshop-subscribe" },
    };

    public Dictionary<string, MaxioCustomer> CustomersByReference { get; } = new(StringComparer.Ordinal);
    public List<MaxioSubscription> Subscriptions { get; } = new();

    public int CreateCustomerCalls { get; private set; }
    public int CreateSubscriptionCalls { get; private set; }
    public List<string> UsedUniquenessTokens { get; } = new();

    /// <summary>
    /// When true, the next CreateSubscriptionAsync call behaves like the loser of a duplicate
    /// race: the subscription IS registered (the "first" request won), but the call itself fails
    /// with Maxio's 409 DuplicatePrevention response.
    /// </summary>
    public bool NextCreateSubscriptionConflict { get; set; }

    public Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MaxioPlan>>(Plans.Where(p => p.ProductFamilyHandle == productFamilyHandle).ToList());

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
        => Task.FromResult(CustomersByReference.TryGetValue(reference, out var customer) ? customer : null);

    public Task<MaxioCustomer> CreateCustomerAsync(SubscriberProfile profile, CancellationToken cancellationToken = default)
    {
        CreateCustomerCalls++;
        if (CustomersByReference.ContainsKey(profile.Reference))
        {
            throw new MaxioApiException(System.Net.HttpStatusCode.UnprocessableEntity, new[] { "Reference has already been taken" });
        }

        var customer = new MaxioCustomer { Id = 900 + CustomersByReference.Count, Reference = profile.Reference, Email = profile.Email };
        CustomersByReference[profile.Reference] = customer;
        return Task.FromResult(customer);
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MaxioSubscription>>(Subscriptions.Where(s => s.CustomerId == customerId).ToList());

    public Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string planHandle, string? subscriptionReference, string uniquenessToken, CancellationToken cancellationToken = default)
    {
        CreateSubscriptionCalls++;
        UsedUniquenessTokens.Add(uniquenessToken);

        var plan = Plans.First(p => p.Handle == planHandle);
        var subscription = new MaxioSubscription
        {
            Id = 5000 + Subscriptions.Count,
            CustomerId = customerId,
            State = "active",
            PlanHandle = plan.Handle,
            PlanName = plan.Name,
            PriceInCents = plan.PriceInCents,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Subscriptions.Add(subscription);

        if (NextCreateSubscriptionConflict)
        {
            NextCreateSubscriptionConflict = false;
            throw new MaxioApiException(System.Net.HttpStatusCode.Conflict, new[] { "DuplicatePrevention::DuplicateSubmissionError" });
        }

        return Task.FromResult(subscription);
    }
}
