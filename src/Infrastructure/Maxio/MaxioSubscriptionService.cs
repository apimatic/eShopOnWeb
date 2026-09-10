using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the subscribe flow against Maxio Advanced Billing. Maxio is the system of
/// record; this service adds idempotency so a double-click never creates a duplicate customer
/// or subscription:
/// <list type="number">
///   <item>subscribe calls are serialized per shopper within the process;</item>
///   <item>the shopper's billing customer is looked up by external reference before creation
///   (and a lost create race is reconciled by re-reading);</item>
///   <item>an existing live subscription to the same plan is returned unchanged;</item>
///   <item>a deterministic uniqueness token guards against duplicate submissions across
///   instances.</item>
/// </list>
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly IMaxioClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IMaxioClient client, MaxioSettings settings, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        _client.ListPlansAsync(_settings.ProductFamilyHandle ?? string.Empty, cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserReference))
        {
            throw new ArgumentException("A user reference is required to subscribe.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            throw new SubscriptionPlanNotFoundException(request.PlanHandle ?? string.Empty);
        }

        // Serialize concurrent subscribe attempts for the same shopper (e.g. double-click) so
        // the existing-subscription check below is authoritative within this process.
        var gate = UserLocks.GetOrAdd(request.UserReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Validate the requested plan is actually offered (also resolves it against the
            // configured product family, preventing subscription to arbitrary products).
            var plans = await _client.ListPlansAsync(_settings.ProductFamilyHandle ?? string.Empty, cancellationToken);
            var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                throw new SubscriptionPlanNotFoundException(request.PlanHandle);
            }

            var customer = await EnsureCustomerAsync(request, cancellationToken);

            // Idempotency: reuse an existing live subscription to the same plan.
            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Shopper {Reference} already has live subscription {SubscriptionId} to {Plan}.",
                    request.UserReference, existing.Id, plan.Handle);
                return new SubscribeResult(existing, alreadySubscribed: true);
            }

            var uniquenessToken = BuildUniquenessToken(request.UserReference, plan.Handle);
            try
            {
                var created = await _client.CreateSubscriptionAsync(customer.Id, plan.Handle, uniquenessToken, cancellationToken);
                _logger.LogInformation("Created subscription {SubscriptionId} for shopper {Reference} to {Plan}.",
                    created.Id, request.UserReference, plan.Handle);
                return new SubscribeResult(created, alreadySubscribed: false);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                // A concurrent/duplicate submission won the race. Reconcile by re-reading.
                var reconciled = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
                if (reconciled is not null)
                {
                    return new SubscribeResult(reconciled, alreadySubscribed: true);
                }
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userReference))
        {
            return Array.Empty<CustomerSubscription>();
        }

        var customer = await _client.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomerReference> EnsureCustomerAsync(SubscribeRequest request, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(request.UserReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ResolveName(request);
        try
        {
            return await _client.CreateCustomerAsync(request.UserReference, request.Email, firstName, lastName, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity || ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Reference uniqueness violation: a concurrent request created the customer first.
            var reconciled = await _client.FindCustomerByReferenceAsync(request.UserReference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }
            throw;
        }
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            s.IsLive && string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static (string FirstName, string LastName) ResolveName(SubscribeRequest request)
    {
        var firstName = string.IsNullOrWhiteSpace(request.FirstName) ? null : request.FirstName!.Trim();
        var lastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName!.Trim();

        if (firstName is not null && lastName is not null)
        {
            return (firstName, lastName);
        }

        // Fall back to the local part of the email so Maxio's required name fields are populated.
        var localPart = request.Email.Split('@')[0];
        return (firstName ?? (string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart),
                lastName ?? "Subscriber");
    }

    private static string BuildUniquenessToken(string userReference, string planHandle)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"eshop-subscribe|{userReference}|{planHandle}"));
        return "eshop-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
