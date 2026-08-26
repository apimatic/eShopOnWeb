using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Orchestrates the subscription billing flows on top of the Maxio API:
/// plan discovery, idempotent customer provisioning and idempotent signup.
/// </summary>
public class SubscriptionBillingService
{
    // States in which an existing subscription to the same plan satisfies a
    // subscribe request (double-click / retry safety). Everything except
    // end-of-life states.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioClient maxioClient,
        IOptions<MaxioOptions> options,
        ILogger<SubscriptionBillingService> logger)
    {
        _maxioClient = maxioClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Lists the purchasable plans: non-archived products in the configured
    /// product family (matched by its stable API handle, never by numeric id).
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsAsync(cancellationToken);

        return products
            .Where(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .Where(p => p.ArchivedAt is null)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<MaxioProduct?> FindPlanAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the Maxio customer for the given eShopOnWeb user, or null when
    /// none exists yet. The eShopOnWeb user id is stored as the Maxio customer
    /// reference, which Maxio enforces as unique.
    /// </summary>
    public Task<MaxioCustomer?> FindCustomerAsync(ApplicationUser user, CancellationToken cancellationToken = default)
        => _maxioClient.FindCustomerByReferenceAsync(CustomerReference(user), cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for the eShopOnWeb user. Idempotent:
    /// looks up by reference first, and if a concurrent create wins the race
    /// (422 on the unique reference) the existing customer is re-read.
    /// </summary>
    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerAsync(user, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName ?? throw new InvalidOperationException("User has no email address.");
        var localPart = email.Split('@')[0];

        try
        {
            return await _maxioClient.CreateCustomerAsync(new MaxioCreateCustomer
            {
                FirstName = localPart,
                LastName = localPart,
                Email = email,
                Reference = CustomerReference(user)
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent signup for the same user — the
            // customer now exists, so read it back instead of failing.
            _logger.LogInformation("Customer create raced for reference {Reference}; re-reading.", CustomerReference(user));
            var winner = await FindCustomerAsync(user, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    /// <summary>
    /// Finds a live (non-terminal) subscription of this customer to the given plan.
    /// </summary>
    public async Task<MaxioSubscription?> FindLiveSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            s.State is not null &&
            !TerminalStates.Contains(s.State));
    }

    /// <summary>
    /// Creates the subscription in Maxio. If a concurrent request already
    /// created one (422), the existing live subscription is returned instead.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCustomer customer, ApplicationUser user, string productHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _maxioClient.CreateSubscriptionAsync(new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customer.Id,
                Reference = SubscriptionReference(user, productHandle),
                PaymentCollectionMethod = _options.PaymentCollectionMethod
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            _logger.LogInformation("Subscription create raced for customer {CustomerId}; re-reading.", customer.Id);
            var winner = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
        => _maxioClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);

    public static string CustomerReference(ApplicationUser user) => user.Id;

    public static string SubscriptionReference(ApplicationUser user, string productHandle) => $"{user.Id}:{productHandle}";
}
