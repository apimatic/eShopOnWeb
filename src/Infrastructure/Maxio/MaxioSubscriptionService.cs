using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription-billing implementation backed by Maxio Advanced Billing as the system of record.
/// Idempotency is anchored on stable references sent to Maxio:
/// - the eShopOnWeb user id is used as the Maxio Customer reference (Maxio enforces its uniqueness),
/// - "{userId}--{productHandle}" is used as the Subscription reference so repeated subscribe
///   attempts for the same user + plan resolve to the same subscription instead of creating duplicates.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>
    /// Subscription states that mean "the user currently holds this subscription" (see the
    /// spec's Subscription-State enum; end-of-life states are excluded).
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "awaiting_signup",
        "past_due", "soft_failure", "unpaid", "paused", "on_hold", "suspended"
    };

    /// <summary>
    /// End-of-life states after which a user may subscribe to the same plan again under a new reference.
    /// </summary>
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly MaxioApiClient _apiClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioApiClient apiClient, IOptions<MaxioOptions> options, ILogger<MaxioSubscriptionService> logger)
    {
        _apiClient = apiClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _apiClient.ListProductsAsync(cancellationToken);
        return products
            .Where(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .Where(p => p.ArchivedAt is null)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(string userId, string email, string productHandle, CancellationToken cancellationToken = default)
    {
        var products = await _apiClient.ListProductsAsync(cancellationToken);
        var product = products.FirstOrDefault(p =>
            string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase) &&
            p.ArchivedAt is null);

        if (product is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var customer = await EnsureCustomerAsync(userId, email, cancellationToken);

        var subscriptionReference = BuildSubscriptionReference(userId, productHandle);
        var existing = await _apiClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null && LiveStates.Contains(existing.State ?? string.Empty))
        {
            _logger.LogInformation("User {UserId} already holds subscription {SubscriptionId} for plan {PlanHandle}.",
                userId, existing.Id, productHandle);
            return new SubscriptionEnrollment
            {
                Subscription = MapSummary(existing),
                AlreadySubscribed = true
            };
        }

        var referenceForCreate = existing is not null && EndOfLifeStates.Contains(existing.State ?? string.Empty)
            ? $"{subscriptionReference}--{Guid.NewGuid():N}"  // previous subscription to this plan ended; new billing term
            : subscriptionReference;

        var created = await _apiClient.CreateSubscriptionAsync(productHandle!, customer.Id, referenceForCreate, cancellationToken);
        _logger.LogInformation("Created Maxio subscription {SubscriptionId} (plan {PlanHandle}) for user {UserId}.",
            created.Id, productHandle, userId);

        return new SubscriptionEnrollment
        {
            Subscription = MapSummary(created),
            AlreadySubscribed = false
        };
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsForUserAsync(string userId, string email, CancellationToken cancellationToken = default)
    {
        var customer = await EnsureCustomerAsync(userId, email, cancellationToken);
        var subscriptions = await _apiClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSummary).ToList();
    }

    /// <summary>
    /// Returns the Maxio customer for the application user, creating it if necessary.
    /// Double-submit safe: Maxio enforces customer-reference uniqueness, so a concurrent
    /// duplicate create fails with 422 and is resolved by re-reading the existing customer.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(string userId, string email, CancellationToken cancellationToken)
    {
        var existing = await _apiClient.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveCustomerName(email);
        try
        {
            return await _apiClient.CreateCustomerAsync(new CreateMaxioCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = userId
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Lost a race against another in-flight create for the same reference — read the winner.
            var winner = await _apiClient.FindCustomerByReferenceAsync(userId, cancellationToken);
            if (winner is not null)
            {
                _logger.LogInformation("Customer create for user {UserId} raced a concurrent create; using customer {CustomerId}.",
                    userId, winner.Id);
                return winner;
            }

            throw;
        }
    }

    private static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"{userId}--{productHandle.ToLowerInvariant()}";

    private static (string FirstName, string LastName) DeriveCustomerName(string email)
    {
        var localPart = email.Split('@', 2)[0];
        var nameParts = localPart
            .Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .Select(ToTitleCase)
            .ToArray();

        return nameParts.Length switch
        {
            0 => ("eShop", "Subscriber"),
            1 => (nameParts[0], "Subscriber"),
            _ => (nameParts[0], string.Join(' ', nameParts.Skip(1)))
        };
    }

    private static string ToTitleCase(string value)
    {
        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        return textInfo.ToTitleCase(value.ToLowerInvariant());
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceCents = product.PriceInCents,
        Price = CentsToDecimal(product.PriceInCents),
        BillingInterval = product.Interval,
        BillingIntervalUnit = product.IntervalUnit ?? string.Empty,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty
    };

    private static SubscriptionSummary MapSummary(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State ?? string.Empty,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        PriceCents = subscription.ProductPriceInCents,
        Price = CentsToDecimal(subscription.ProductPriceInCents),
        BillingInterval = subscription.Product?.Interval ?? 0,
        BillingIntervalUnit = subscription.Product?.IntervalUnit,
        ActivatedAt = subscription.ActivatedAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        CanceledAt = subscription.CanceledAt,
        CustomerId = subscription.Customer?.Id
    };

    private static decimal CentsToDecimal(long cents) => cents / 100m;
}
