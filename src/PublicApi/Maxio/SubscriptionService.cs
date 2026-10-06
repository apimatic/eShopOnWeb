using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Orchestrates the Maxio subscription flows for eShopOnWeb users: listing plans,
/// idempotently ensuring a Maxio customer exists, subscribing (idempotently), and
/// listing a user's subscriptions. Maxio is the system of record; the eShopOnWeb user id
/// is used as the Maxio customer reference so lookups are stable across restarts.
/// </summary>
public class SubscriptionService
{
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly MaxioApiClient _maxio;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(MaxioApiClient maxio, IOptions<MaxioOptions> options, ILogger<SubscriptionService> logger)
    {
        _maxio = maxio;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Lists the subscription plans available in the configured product family.
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        return await _maxio.ListProductsByFamilyAsync(_options.Value.ProductFamilyHandle, cancellationToken);
    }

    /// <summary>
    /// Returns the Maxio customer for the given eShopOnWeb user, creating one if it does not
    /// exist yet. Idempotent: the user id is used as the customer reference, and a concurrent
    /// duplicate create is recovered by re-looking-up the customer.
    /// </summary>
    public async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var reference = BuildCustomerReference(user);
        var customer = await _maxio.LookupCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        try
        {
            customer = await _maxio.CreateCustomerAsync(new CreateMaxioCustomerRequest
            {
                Customer = new MaxioCustomerAttributes
                {
                    FirstName = user.UserName ?? "eShop",
                    LastName = "User",
                    Email = user.Email ?? user.UserName ?? string.Empty,
                    Organization = "eShopOnWeb",
                    Reference = reference
                }
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request created the customer first; re-lookup instead of failing.
            customer = await _maxio.LookupCustomerByReferenceAsync(reference, cancellationToken);
            if (customer is null)
            {
                _logger.LogError(ex, "Maxio customer creation failed for user {UserId} and the customer could not be re-looked-up.", user.Id);
                throw;
            }
        }

        _logger.LogInformation("Ensured Maxio customer {CustomerId} for eShopOnWeb user {UserId}.", customer.Id, user.Id);
        return customer;
    }

    /// <summary>
    /// Subscribes the user to the given plan. Idempotent: an existing live subscription to the
    /// plan is returned as-is, and a deterministic uniqueness token prevents a double-click
    /// from creating a second subscription.
    /// </summary>
    public async Task<MaxioSubscription> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken = default)
    {
        var customer = await EnsureCustomerAsync(user, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customer.Id, planHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("User {UserId} already has subscription {SubscriptionId} to plan {PlanHandle}.", user.Id, existing.Id, planHandle);
            return existing;
        }

        var uniquenessToken = BuildUniquenessToken(user, planHandle);
        try
        {
            var created = await _maxio.CreateSubscriptionAsync(customer.Id, planHandle, uniquenessToken, cancellationToken);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}.", created.Id, user.Id, planHandle);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // A duplicate request won the race; return the subscription that was created.
            var afterConflict = await FindLiveSubscriptionAsync(customer.Id, planHandle, cancellationToken);
            if (afterConflict is not null)
            {
                _logger.LogInformation("Recovered from duplicate subscription request for user {UserId} on plan {PlanHandle}.", user.Id, planHandle);
                return afterConflict;
            }
            throw;
        }
    }

    /// <summary>
    /// Lists the subscriptions belonging to the user. Returns an empty list when the user has
    /// no Maxio customer record yet.
    /// </summary>
    public async Task<IReadOnlyList<MaxioSubscription>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var reference = BuildCustomerReference(user);
        var customer = await _maxio.LookupCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            !TerminalStates.Contains(s.State ?? string.Empty) &&
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildCustomerReference(ApplicationUser user) => $"eshop-{user.Id}";

    private static string BuildUniquenessToken(ApplicationUser user, string planHandle)
    {
        var input = $"eshop-subscription:{user.Id}:{planHandle}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }
}
