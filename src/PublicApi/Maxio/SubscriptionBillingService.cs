using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Orchestrates the subscription-billing flows against Maxio Advanced Billing:
/// listing plans from the configured product family, ensuring a Maxio customer
/// exists for an eShopOnWeb user, and enrolling them idempotently.
/// </summary>
public class SubscriptionBillingService
{
    // States in which an existing subscription must block creating a duplicate.
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "trial_ended", "failed_to_create"
    };

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioClient maxioClient,
        IOptions<MaxioSettings> settings,
        ILogger<SubscriptionBillingService> logger)
    {
        _maxioClient = maxioClient;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>Lists the purchasable plans (non-archived products in the configured family).</summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products.Where(p => p.ArchivedAt is null).ToList();
    }

    /// <summary>
    /// Subscribes the given user to a plan. Idempotent: the Maxio customer is looked up
    /// (and only created when missing) by a reference derived from the eShopOnWeb username,
    /// and the subscription carries a deterministic reference so a retried/double-submitted
    /// request returns the existing subscription instead of creating a second one.
    /// </summary>
    public async Task<MaxioSubscription> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken = default)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new MaxioPlanNotFoundException(productHandle);
        }

        var customer = await EnsureCustomerAsync(username, cancellationToken);

        var subscriptionReference = BuildSubscriptionReference(customer.Reference!, plan.Handle!);
        var existing = await _maxioClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null && !IsEndOfLife(existing.State))
        {
            _logger.LogInformation(
                "Subscription {SubscriptionId} already exists for reference {Reference}; returning it instead of creating a duplicate.",
                existing.Id, subscriptionReference);
            return existing;
        }

        // A prior subscription under the deterministic reference reached an end-of-life
        // state; suffix the new one so lookup-by-reference stays unambiguous.
        var reference = existing is null
            ? subscriptionReference
            : $"{subscriptionReference}:{Guid.NewGuid():N}";

        var created = await _maxioClient.CreateSubscriptionAsync(new MaxioSubscriptionAttributes
        {
            ProductHandle = plan.Handle!,
            CustomerReference = customer.Reference!,
            Reference = reference,
            // Invoice-based (remittance) collection: the seeded plans require no payment
            // method, so signup must not attempt an automatic card charge.
            PaymentCollectionMethod = "remittance"
        }, cancellationToken);

        _logger.LogInformation(
            "Created Maxio subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
            created.Id, customer.Id, plan.Handle);

        return created;
    }

    /// <summary>Lists the caller's subscriptions, or an empty list if they have never subscribed.</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListMySubscriptionsAsync(string username, CancellationToken cancellationToken = default)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(BuildCustomerReference(username), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string username, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(username);

        var existing = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(username);
        try
        {
            return await _maxioClient.CreateCustomerAsync(new MaxioCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = username,
                Reference = reference
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent request that created the customer first.
            var winner = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    // eShopOnWeb usernames are email addresses; the Maxio customer reference is derived
    // from it so the mapping is deterministic without needing local persistence.
    private static string BuildCustomerReference(string username) => $"eshoponweb:{username.Trim().ToLowerInvariant()}";

    private static string BuildSubscriptionReference(string customerReference, string productHandle)
        => $"{customerReference}:{productHandle.Trim().ToLowerInvariant()}";

    private static bool IsEndOfLife(string? state) => state is not null && EndOfLifeStates.Contains(state);

    private static (string FirstName, string LastName) DeriveName(string username)
    {
        var localPart = username.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("eShop", "Shopper"),
            1 => (Capitalize(parts[0]), "Shopper"),
            _ => (Capitalize(parts[0]), Capitalize(parts[^1]))
        };
    }

    private static string Capitalize(string value)
        => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);
}
