using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Maxio enforces uniqueness on the customer `reference` (a second create for the same
    // reference is rejected with 422), but it does NOT enforce any uniqueness between a
    // customer and product on subscription create - two truly concurrent requests (not just a
    // sequential double-click, but e.g. two tabs submitting at the same instant) would each
    // pass the "does an active subscription already exist" check before either had written one,
    // and Maxio would happily create two. Serializing per-buyer here closes that race for this
    // single-instance deployment (no distributed lock infra is available/allowed - see
    // PHASE-BUILD.md). The 422-retry fallbacks below remain as defense-in-depth.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();

    private readonly IMaxioClient _client;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(IMaxioClient client, MaxioOptions options)
    {
        _client = client;
        _options = options;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products.Where(p => p.ArchivedAt is null).ToList();
    }

    public async Task<(MaxioSubscription Subscription, bool Created)> SubscribeAsync(
        string buyerReference,
        string buyerEmail,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        var plan = await FindPlanAsync(planHandle, cancellationToken);

        var enrollmentLock = EnrollmentLocks.GetOrAdd(buyerReference, static _ => new SemaphoreSlim(1, 1));
        await enrollmentLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(buyerReference, buyerEmail, cancellationToken);

            var existingSubscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = existingSubscriptions.FirstOrDefault(s => s.ProductId == plan.Id && s.IsActiveEnrollment);
            if (existing is not null)
            {
                return (existing, false);
            }

            try
            {
                var created = await _client.CreateSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
                return (created, true);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // Fall back to whatever now exists rather than treating this as a hard failure,
                // e.g. if the subscription was created out-of-band between our check and this call.
                var afterConflict = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
                var matched = afterConflict.FirstOrDefault(s => s.ProductId == plan.Id && s.IsActiveEnrollment);
                if (matched is not null)
                {
                    return (matched, false);
                }

                throw;
            }
        }
        finally
        {
            enrollmentLock.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForBuyerAsync(string buyerReference, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(buyerReference, cancellationToken);
        if (customer is null)
        {
            return [];
        }

        return await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioProduct> FindPlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await GetAvailablePlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => p.Handle.Equals(planHandle, System.StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new PlanNotFoundException(planHandle);
        }

        return plan;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string buyerReference, string buyerEmail, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(buyerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(buyerReference);

        try
        {
            return await _client.CreateCustomerAsync(
                new MaxioCreateCustomer(buyerReference, buyerEmail, firstName, lastName),
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request (e.g. a double-click) may have created the customer for this
            // reference between our lookup and this call. Fall back to the now-existing record
            // rather than creating a duplicate.
            var afterConflict = await _client.FindCustomerByReferenceAsync(buyerReference, cancellationToken);
            if (afterConflict is not null)
            {
                return afterConflict;
            }

            throw;
        }
    }

    private static (string FirstName, string LastName) SplitDisplayName(string reference)
    {
        var localPart = reference.Split('@')[0];
        var segments = localPart.Split(['.', '_', '-'], System.StringSplitOptions.RemoveEmptyEntries);

        return segments.Length >= 2
            ? (Capitalize(segments[0]), Capitalize(segments[^1]))
            : (Capitalize(localPart), "eShopOnWeb Customer");
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
