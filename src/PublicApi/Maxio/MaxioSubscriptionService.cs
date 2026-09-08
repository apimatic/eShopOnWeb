using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>The outcome of an idempotent subscribe call.</summary>
public sealed record SubscriptionEnrollmentResult(MaxioSubscription Subscription, bool Created);

/// <summary>
/// Orchestrates the hero "Subscribe" flow against Maxio Advanced Billing:
/// browse plans, ensure a Maxio customer exists for the eShopOnWeb user (idempotent),
/// enroll the user on a plan (idempotent), and list the user's subscriptions.
///
/// Maxio is the billing system of record. The link between an eShopOnWeb user and a
/// Maxio customer is the customer <c>reference</c> attribute, which this service sets to
/// the eShopOnWeb user id. Because Maxio enforces a unique reference per site, a user can
/// never produce two Maxio customers, even across app restarts or concurrent requests.
/// Subscription creation is additionally serialized per user in-process so a double-click
/// returns the already-created subscription instead of creating a second one.
/// </summary>
public class MaxioSubscriptionService
{
    // Maxio collection method used so enrolling never requires card capture / 3-DS.
    private const string RemittanceCollectionMethod = "remittance";

    // Subscription states that mean "the plan is currently in force" for the user.
    private static readonly HashSet<string> InForceStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "trialing", "active", "past_due", "unpaid", "on_hold", "assessing"
    };

    private readonly MaxioApiClient _maxio;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    // One in-process semaphore per user id serializes the check-then-create subscribe path.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    public MaxioSubscriptionService(MaxioApiClient maxio, MaxioOptions options, ILogger<MaxioSubscriptionService> logger)
    {
        _maxio = maxio;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Lists the currently subscribable plans in the configured product family: not archived and
    /// not requiring a payment method (so enrollment works without card capture).
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListAvailablePlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);

        return products
            .Where(IsInConfiguredFamily)
            .Where(p => p.ArchivedAt == null && !p.RequireCreditCard)
            .OrderBy(p => p.PriceInCents ?? long.MaxValue)
            .ThenBy(p => p.Handle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Enrolls the user on the given plan. Returns the resulting (or pre-existing) subscription.
    /// Guarantees that repeated/concurrent calls for the same user + plan never create more than
    /// one in-force subscription.
    /// </summary>
    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(
        ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        var semaphore = UserLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var availablePlans = await ListAvailablePlansAsync(cancellationToken);
            if (availablePlans.All(p => !string.Equals(p.Handle, planHandle, StringComparison.Ordinal)))
            {
                throw new SubscriptionPlanNotFoundException(planHandle);
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);

            var subscriptions = await _maxio.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
            var existing = subscriptions.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                s.State != null && InForceStates.Contains(s.State));

            if (existing != null)
            {
                _logger.LogInformation("User {UserId} already has an in-force subscription {SubscriptionId} for plan {PlanHandle}; returning it.",
                    user.Id, existing.Id, planHandle);
                return new SubscriptionEnrollmentResult(existing, Created: false);
            }

            var created = await _maxio.CreateSubscriptionAsync(new MaxioSubscriptionDraft
            {
                ProductHandle = planHandle,
                CustomerReference = user.Id,
                PaymentCollectionMethod = RemittanceCollectionMethod
            }, cancellationToken);

            _logger.LogInformation("User {UserId} subscribed to plan {PlanHandle}; Maxio subscription {SubscriptionId} created (state {State}).",
                user.Id, planHandle, created.Id, created.State);

            return new SubscriptionEnrollmentResult(created, Created: true);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Lists the subscriptions the user currently holds in the configured product family.
    /// A user that has never subscribed has no Maxio customer and yields an empty list.
    /// </summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = await _maxio.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);

        return subscriptions
            .Where(s => IsInConfiguredFamily(s.Product))
            .OrderByDescending(s => s.CurrentPeriodEndsAt)
            .ThenByDescending(s => s.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Returns the Maxio customer backing the given user, creating one (idempotently, keyed by the
    /// user id stored as the customer reference) when none exists yet.
    /// </summary>
    public async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveDisplayName(user);

        try
        {
            var created = await _maxio.CreateCustomerAsync(new MaxioCustomerDraft
            {
                FirstName = firstName,
                LastName = lastName,
                Email = user.Email ?? user.UserName,
                Reference = user.Id
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for eShopOnWeb user {UserId}.", created.Id, user.Id);
            return created;
        }
        catch (MaxioApiException ex) when (IsReferenceConflict(ex))
        {
            // A concurrent request on another node created the customer first. Re-read it.
            var winner = await _maxio.FindCustomerByReferenceAsync(user.Id, cancellationToken);
            return winner
                ?? throw new MaxioApiException(ex.StatusCode,
                    $"Maxio reported a customer reference conflict for user '{user.Id}' but the customer could not be read back.", ex.Errors);
        }
    }

    private bool IsInConfiguredFamily(MaxioProduct? product) =>
        product?.ProductFamily != null &&
        string.Equals(product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase);

    private static bool IsReferenceConflict(MaxioApiException exception) =>
        exception.StatusCode == 422 &&
        exception.Errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase));

    // eShopOnWeb's ApplicationUser does not collect first/last names; the Maxio customer name is
    // derived deterministically from the sign-in identity (email/username) so customer creation
    // stays stable and idempotent. In a real deployment this would read the user's profile name.
    private static (string FirstName, string LastName) DeriveDisplayName(ApplicationUser user)
    {
        var identity = string.IsNullOrWhiteSpace(user.UserName) ? user.Email : user.UserName;
        if (string.IsNullOrWhiteSpace(identity))
        {
            return (user.Id, "eShopUser");
        }

        var at = identity.IndexOf('@');
        if (at <= 0)
        {
            return (identity, "eShopUser");
        }

        var localPart = identity[..at];
        var domain = identity[(at + 1)..];
        var lastDot = domain.LastIndexOf('.');
        var lastName = lastDot > 0 ? domain[..lastDot] : domain;

        return (localPart, lastName);
    }
}
