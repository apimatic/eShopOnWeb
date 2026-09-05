using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Subscription states in which the buyer is considered already enrolled in a plan.
    // See https://maxio.zendesk.com/hc/en-us/articles/24252119027853-Subscription-States
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "trialing", "assessing", "active", "soft_failure", "past_due", "awaiting_signup"
    };

    private readonly IMaxioClient _maxioClient;
    private readonly IRepository<MaxioCustomerLink> _customerLinks;

    public MaxioSubscriptionService(IMaxioClient maxioClient, IRepository<MaxioCustomerLink> customerLinks)
    {
        _maxioClient = maxioClient;
        _customerLinks = customerLinks;
    }

    public Task<IReadOnlyList<MaxioPlanDto>> GetAvailablePlansAsync(CancellationToken cancellationToken = default) =>
        _maxioClient.ListPlansAsync(cancellationToken);

    public async Task<MaxioSubscriptionDto> SubscribeAsync(string buyerId, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        var customerId = await EnsureCustomerAsync(buyerId, email, cancellationToken);

        var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        var alreadySubscribed = existingSubscriptions.FirstOrDefault(subscription =>
            string.Equals(subscription.ProductHandle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            LiveStates.Contains(subscription.State));

        if (alreadySubscribed != null)
        {
            return alreadySubscribed;
        }

        return await _maxioClient.CreateSubscriptionAsync(customerId, planHandle, cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioSubscriptionDto>> GetMySubscriptionsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var link = await _customerLinks.FirstOrDefaultAsync(new MaxioCustomerLinkByBuyerIdSpecification(buyerId), cancellationToken);
        if (link == null)
        {
            return Array.Empty<MaxioSubscriptionDto>();
        }

        return await _maxioClient.ListCustomerSubscriptionsAsync(link.MaxioCustomerId, cancellationToken);
    }

    private async Task<int> EnsureCustomerAsync(string buyerId, string email, CancellationToken cancellationToken)
    {
        var spec = new MaxioCustomerLinkByBuyerIdSpecification(buyerId);
        var link = await _customerLinks.FirstOrDefaultAsync(spec, cancellationToken);
        if (link != null)
        {
            return link.MaxioCustomerId;
        }

        var (firstName, lastName) = SplitDisplayName(email);
        var customer = await _maxioClient.EnsureCustomerAsync(buyerId, email, firstName, lastName, cancellationToken);

        // A concurrent request (e.g. a double-click) may have persisted the link while the Maxio
        // round-trip above was in flight; re-check before inserting a second local row for it.
        link = await _customerLinks.FirstOrDefaultAsync(spec, cancellationToken);
        if (link != null)
        {
            return link.MaxioCustomerId;
        }

        await _customerLinks.AddAsync(new MaxioCustomerLink(buyerId, customer.Id), cancellationToken);
        return customer.Id;
    }

    private static (string FirstName, string LastName) SplitDisplayName(string email)
    {
        var localPart = email.Split('@')[0];
        return (localPart, "eShopOnWeb");
    }
}
