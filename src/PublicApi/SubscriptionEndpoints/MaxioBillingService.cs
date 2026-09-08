using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Orchestrates the Maxio operations behind the subscription endpoints: it guarantees a
/// Maxio customer exists for an eShopOnWeb user and that a subscription to a plan is
/// created at most once (idempotent under retries and double-clicks).
/// </summary>
public interface IMaxioBillingService
{
    Task<IReadOnlyList<MaxioProduct>> ListAvailablePlansAsync(CancellationToken cancellationToken);

    Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the Maxio customer for the given eShopOnWeb user, creating one (idempotently)
    /// when it does not yet exist.
    /// </summary>
    Task<MaxioCustomer> EnsureCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken cancellationToken);

    /// <summary>Returns the Maxio customer for the user, or null when none exists yet.</summary>
    Task<MaxioCustomer?> FindCustomerAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Ensures the user (via <paramref name="customer"/>) has a subscription to the plan
    /// identified by <paramref name="planHandle"/>. Creating is idempotent: when the user
    /// already has a subscription to that plan the existing subscription is returned and
    /// <see cref="SubscriptionResult.WasCreated"/> is false.
    /// </summary>
    Task<SubscriptionResult> SubscribeToPlanAsync(MaxioCustomer customer, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListUserSubscriptionsAsync(MaxioCustomer customer, CancellationToken cancellationToken);
}

public sealed class SubscriptionResult
{
    public SubscriptionResult(MaxioSubscription subscription, bool wasCreated)
    {
        Subscription = subscription;
        WasCreated = wasCreated;
    }

    public MaxioSubscription Subscription { get; }

    public bool WasCreated { get; }
}

/// <summary>
/// Default <see cref="IMaxioBillingService"/> implementation. All reference values are
/// derived from the eShopOnWeb user id so that Maxio itself is the system of record and
/// re-runs against the same sandbox stay consistent.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    private readonly IMaxioClient _client;
    private readonly MaxioOptions _options;

    public MaxioBillingService(IMaxioClient client, IOptions<MaxioOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public Task<IReadOnlyList<MaxioProduct>> ListAvailablePlansAsync(CancellationToken cancellationToken)
    {
        return _client.ListFamilyProductsAsync(_options.ResolveProductFamilyHandle(), cancellationToken);
    }

    public Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        return _client.GetSiteCurrencyAsync(cancellationToken);
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(userId);

        var existing = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var body = new MaxioCreateCustomerBody
            {
                Customer = new MaxioNewCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Organization = email,
                    Reference = reference
                },
                UniquenessToken = $"eshop:customer:{userId}"
            };

            return await _client.CreateCustomerAsync(body, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.IsReferenceTakenError || ex.IsDuplicateSubmissionError)
        {
            // Another (racing) request created the customer first; recover by looking it up.
            return await _client.FindCustomerByReferenceAsync(reference, cancellationToken)
                   ?? throw new MaxioApiException(ex.StatusCode,
                       "Maxio reported a duplicate customer but the customer could not be found afterwards.", ex.Errors);
        }
    }

    public Task<MaxioCustomer?> FindCustomerAsync(string userId, CancellationToken cancellationToken)
    {
        return _client.FindCustomerByReferenceAsync(BuildCustomerReference(userId), cancellationToken);
    }

    public async Task<SubscriptionResult> SubscribeToPlanAsync(MaxioCustomer customer, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioApiException(400, "planHandle is required.");
        }

        var plan = await _client.FindProductByHandleAsync(planHandle, cancellationToken);
        if (plan is null)
        {
            throw new MaxioApiException(400, $"The plan '{planHandle}' does not exist on the Maxio site.");
        }

        var familyHandle = _options.ResolveProductFamilyHandle();
        if (!string.IsNullOrEmpty(plan.ProductFamily?.Handle) &&
            !string.Equals(plan.ProductFamily.Handle, familyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new MaxioApiException(400,
                $"The plan '{planHandle}' does not belong to the configured product family '{familyHandle}'.");
        }

        if (string.IsNullOrEmpty(customer.Reference))
        {
            throw new MaxioApiException(500, "The Maxio customer has no reference; cannot guarantee an idempotent subscription.");
        }

        var subscriptionReference = BuildSubscriptionReference(customer.Reference, planHandle);

        var existing = await _client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return new SubscriptionResult(existing, wasCreated: false);
        }

        var body = new MaxioCreateSubscriptionBody
        {
            Subscription = new MaxioNewSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customer.Id,
                Reference = subscriptionReference,
                NextBillingAt = BuildNextBillingAt(plan)
            },
            UniquenessToken = $"eshop:subscription:{customer.Reference}:{planHandle}"
        };

        try
        {
            var created = await _client.CreateSubscriptionAsync(body, cancellationToken);
            return new SubscriptionResult(created, wasCreated: true);
        }
        catch (MaxioApiException ex) when (ex.IsReferenceTakenError || ex.IsDuplicateSubmissionError)
        {
            var winner = await _client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (winner is not null)
            {
                return new SubscriptionResult(winner, wasCreated: false);
            }

            throw;
        }
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListUserSubscriptionsAsync(MaxioCustomer customer, CancellationToken cancellationToken)
    {
        return _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    internal static string BuildCustomerReference(string userId) => $"eshop:user:{userId}";

    internal static string BuildSubscriptionReference(string customerReference, string planHandle) =>
        $"eshop:sub:{customerReference}:{planHandle}";

    /// <summary>
    /// The seeded plans require no payment method but have a monthly price, so subscribing
    /// without a card must not trigger an immediate charge. When the product has no trial we
    /// schedule the first billing one full interval out (which Maxio honors by not capturing
    /// any signup payment). Products with a trial keep their natural trial semantics.
    /// </summary>
    private static string? BuildNextBillingAt(MaxioProduct plan)
    {
        if (plan.TrialInterval is > 0)
        {
            return null;
        }

        var interval = Math.Max(1, plan.Interval ?? 1);
        var intervalUnit = string.IsNullOrWhiteSpace(plan.IntervalUnit) ? "month" : plan.IntervalUnit;

        DateTime nextBillingUtc = intervalUnit switch
        {
            "day" => DateTime.UtcNow.AddDays(interval),
            _ => DateTime.UtcNow.AddMonths(interval)
        };

        return nextBillingUtc.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);
    }
}
