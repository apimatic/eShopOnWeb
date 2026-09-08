using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Orchestrates subscription billing against Maxio Advanced Billing for the PublicApi host.
///
/// <para>Maxio is the system of record: no subscription data is mirrored into the local database.
/// The eShopOnWeb shopper is linked to a Maxio customer through the customer <c>reference</c>
/// attribute, which is set to the shopper's (normalized) e-mail address. The reference is unique
/// per Maxio site, which is what makes customer creation idempotent even when two requests race.</para>
///
/// <para>Subscribing is made idempotent two ways:
/// <list type="number">
/// <item>a per-shopper in-process lock serializes subscribe attempts for the same shopper, so a
/// double-click cannot both pass the "no active subscription" check before either completes; and</item>
/// <item>before creating a subscription the existing Maxio subscriptions of the shopper are checked
/// and an open subscription to the requested plan is returned instead of creating a duplicate.</item>
/// </list></para>
/// </summary>
public class MaxioSubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new(StringComparer.OrdinalIgnoreCase);

    // States in which the shopper is still considered to "hold" the plan. Subscriptions that have
    // been cancelled, expired, or failed to create do not block a (re)subscribe.
    private static readonly HashSet<string> EndedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly MaxioClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioClient client, IOptions<MaxioOptions> options, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// All currently purchasable plans in the configured product family.
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = _options.RequireProductFamilyHandle();
        var products = await _client.ListProductsForFamilyAsync(familyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Looks up a single plan by handle and verifies it is offered by this app (belongs to the
    /// configured product family and is not archived). Returns <c>null</c> when the plan is not offered.
    /// </summary>
    public async Task<MaxioProduct?> FindOfferedPlanAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            return null;
        }

        MaxioProduct? product;
        try
        {
            product = await _client.GetProductByHandleAsync(productHandle.Trim(), cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.IsNotFound)
        {
            return null;
        }

        if (product is null || product.ArchivedAt is not null)
        {
            return null;
        }

        var familyHandle = _options.RequireProductFamilyHandle();
        var productFamilyHandle = product.ProductFamily?.Handle;
        if (!string.Equals(productFamilyHandle, familyHandle, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Plan {PlanHandle} belongs to product family '{ProductFamilyHandle}', not the configured family '{ExpectedFamilyHandle}'; treating it as not offered.",
                product.Handle, productFamilyHandle, familyHandle);
            return null;
        }

        return product;
    }

    /// <summary>
    /// Ensures a Maxio customer exists for the shopper and subscribes them to the plan. When the
    /// shopper already holds an open subscription to the plan the existing subscription is returned
    /// (<paramref name="created"/> is <c>false</c>); otherwise a new subscription is created.
    /// </summary>
    /// <exception cref="ArgumentException">When the plan is not offered by this app.</exception>
    public async Task<(MaxioSubscription Subscription, bool Created)> SubscribeAsync(
        string shopperEmail, string productHandle, CancellationToken cancellationToken = default)
    {
        var plan = await FindOfferedPlanAsync(productHandle, cancellationToken)
            ?? throw new ArgumentException($"The plan '{productHandle}' is not available to subscribe to.", nameof(productHandle));

        var reference = ShopperReference(shopperEmail);
        var @lock = SubscribeLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));

        await @lock.WaitAsync(cancellationToken);
        try
        {
            var customer = await FindOrCreateCustomerAsync(shopperEmail, reference, cancellationToken);

            var openSubscription = await FindOpenSubscriptionAsync(customer, plan, cancellationToken);
            if (openSubscription is not null)
            {
                _logger.LogInformation(
                    "Shopper '{Reference}' already holds subscription {SubscriptionId} to plan '{PlanHandle}'; returning the existing subscription.",
                    reference, openSubscription.Id, plan.Handle);
                return (openSubscription, Created: false);
            }

            var attributes = new CreateSubscriptionAttributes
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                // Products with a trial start in the trial state (no payment profile needed), so a
                // deferred first billing date is only applied to non-trial products.
                NextBillingAt = (plan.TrialInterval ?? 0) > 0 ? null : FirstBillingDate(plan)
            };

            try
            {
                var created = await _client.CreateSubscriptionAsync(attributes, cancellationToken);
                _logger.LogInformation(
                    "Created subscription {SubscriptionId} for shopper '{Reference}' on plan '{PlanHandle}'.",
                    created.Id, reference, plan.Handle);
                return (created, Created: true);
            }
            catch (MaxioApiException ex) when (ex.IsUnprocessableEntity)
            {
                // Maxio does not expose a client-generated idempotency key, so a concurrent request
                // (for example from another app instance) may have created the subscription between
                // our check and our create. Treat that as an idempotent success.
                var raced = await FindOpenSubscriptionAsync(customer, plan, cancellationToken);
                if (raced is not null)
                {
                    _logger.LogWarning(
                        "Subscription create for shopper '{Reference}' on plan '{PlanHandle}' returned 422 ({Message}) but an open subscription {SubscriptionId} exists; returning the existing subscription.",
                        reference, plan.Handle, ex.Message, raced.Id);
                    return (raced, Created: false);
                }

                throw;
            }
        }
        finally
        {
            @lock.Release();
        }
    }

    /// <summary>
    /// Lists the Maxio subscriptions for a shopper. When no Maxio customer exists yet for the
    /// shopper an empty list is returned (no customer is created by listing).
    /// </summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(
        string shopperEmail, CancellationToken cancellationToken = default)
    {
        var reference = ShopperReference(shopperEmail);
        var customer = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    private async Task<MaxioCustomer> FindOrCreateCustomerAsync(
        string shopperEmail, string reference, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var customer = new MaxioCustomer
        {
            FirstName = ShopperFirstName(shopperEmail),
            LastName = "User",
            Email = shopperEmail,
            Reference = reference,
            Organization = "eShopOnWeb"
        };

        try
        {
            var created = await _client.CreateCustomerAsync(customer, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for shopper '{Reference}'.", created.Id, reference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsUnprocessableEntity)
        {
            // Two requests raced to create the same customer; the unique reference constraint means
            // only one wins. The loser re-reads the winner's record.
            var winner = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (winner is not null)
            {
                _logger.LogInformation(
                    "Customer create for shopper '{Reference}' returned 422 but the customer already exists ({CustomerId}); using the existing customer.",
                    reference, winner.Id);
                return winner;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindOpenSubscriptionAsync(
        MaxioCustomer customer, MaxioProduct plan, CancellationToken cancellationToken)
    {
        if (customer.Id is null || string.IsNullOrWhiteSpace(plan.Handle))
        {
            return null;
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        return subscriptions.FirstOrDefault(s => !IsEnded(s.State) && string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsEnded(string? state) =>
        !string.IsNullOrWhiteSpace(state) && EndedStates.Contains(state);

    private static DateTimeOffset FirstBillingDate(MaxioProduct plan)
    {
        var now = DateTimeOffset.UtcNow;
        var interval = plan.Interval ?? 1;
        var unit = plan.IntervalUnit ?? "month";

        // The create-subscription operation treats a future next_billing_at as "no initial charge,
        // first assessment at this timestamp". Aligning it with the plan cadence gives the shopper
        // a subscription whose next billing date is exactly one billing cycle away.
        return string.Equals(unit, "day", StringComparison.OrdinalIgnoreCase)
            ? now.AddDays(interval)
            : now.AddMonths(interval);
    }

    private static string ShopperReference(string shopperEmail)
    {
        if (string.IsNullOrWhiteSpace(shopperEmail))
        {
            throw new ArgumentException("A shopper e-mail is required to manage Maxio subscriptions.", nameof(shopperEmail));
        }

        return shopperEmail.Trim().ToLowerInvariant();
    }

    private static string ShopperFirstName(string shopperEmail)
    {
        var email = shopperEmail.Trim();
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        return string.IsNullOrWhiteSpace(local) ? "Shopper" : local;
    }
}
