using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionService : ISubscriptionService
{
    // Subscriptions in these states are done - they no longer count as "already enrolled"
    // when deciding whether a subscribe request should reuse an existing subscription.
    private static readonly string[] TerminalStates = { "canceled", "expired", "failed_to_create", "trial_ended" };

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _maxioOptions;

    public SubscriptionService(IMaxioClient maxioClient, MaxioOptions maxioOptions)
    {
        _maxioClient = maxioClient;
        _maxioOptions = maxioOptions;
    }

    public Task<IReadOnlyList<MaxioPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default) =>
        _maxioClient.ListPlansAsync(_maxioOptions.ProductFamilyHandle, cancellationToken);

    public async Task<MaxioSubscription> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        var plans = await _maxioClient.ListPlansAsync(_maxioOptions.ProductFamilyHandle, cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new MaxioApiException(404, $"No plan with handle '{planHandle}' was found in product family '{_maxioOptions.ProductFamilyHandle}'.");
        }

        var (firstName, lastName) = SplitDisplayName(userName);
        var customer = await _maxioClient.EnsureCustomerAsync(userName, userName, firstName, lastName, cancellationToken);

        var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
            !TerminalStates.Contains(s.State));
        if (existing is not null)
        {
            return existing;
        }

        return await _maxioClient.CreateSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var customer = await _maxioClient.FindCustomerByReferenceAsync(userName, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private static (string FirstName, string LastName) SplitDisplayName(string userName)
    {
        var localPart = userName.Split('@')[0];
        return (localPart, "Subscriber");
    }
}
