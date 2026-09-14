using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Provisions Maxio customers for eShopOnWeb users and enrolls them in plans. A double-click (or
/// otherwise duplicated request) never creates two customers or two subscriptions to the same plan:
/// customer creation always looks up by reference first, and subscribing checks for an existing
/// live subscription to the plan before creating a new one. A per-reference lock serializes
/// concurrent requests from the same user within this process so the two checks above can't race
/// against another request from the same double-click.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocksByUserReference = new();

    private readonly IMaxioApiClient _maxioApiClient;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(IMaxioApiClient maxioApiClient, IOptions<MaxioOptions> options)
    {
        _maxioApiClient = maxioApiClient;
        _options = options.Value;
    }

    public Task<IReadOnlyList<MaxioPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default) =>
        _maxioApiClient.ListPlansAsync(_options.ProductFamilyHandle, cancellationToken);

    public async Task<MaxioSubscription> SubscribeAsync(string userReference, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        var gate = LocksByUserReference.GetOrAdd(userReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await GetOrCreateCustomerAsync(userReference, email, cancellationToken);

            var existingSubscriptions = await _maxioApiClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = existingSubscriptions.FirstOrDefault(s =>
                string.Equals(s.Plan?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) && IsLive(s.State));
            if (existing is not null)
            {
                return existing;
            }

            return await _maxioApiClient.CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default)
    {
        var customer = await _maxioApiClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _maxioApiClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(string userReference, string email, CancellationToken cancellationToken)
    {
        var existing = await _maxioApiClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitForMaxio(email);
        try
        {
            return await _maxioApiClient.CreateCustomerAsync(userReference, email, firstName, lastName, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio only allows one customer per reference. A 422 here most likely means another
            // request for the same user won the race and already created it - use that one.
            var afterRace = await _maxioApiClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
            if (afterRace is not null)
            {
                return afterRace;
            }

            throw;
        }
    }

    // eShopOnWeb's identity model has no first/last name, only an email; Maxio requires both.
    private static (string FirstName, string LastName) SplitForMaxio(string email)
    {
        var localPart = email.Split('@')[0];
        return (string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart, "Customer");
    }

    private static bool IsLive(string state) => state is not ("canceled" or "expired" or "failed_to_create");
}
