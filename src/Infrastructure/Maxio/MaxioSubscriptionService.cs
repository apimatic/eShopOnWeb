using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing-backed implementation of <see cref="ISubscriptionService"/>.
/// Maxio is the billing system of record; eShopOnWeb users are mapped to Maxio
/// customers through stable references, making every operation idempotent.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>Prefix for Maxio customer references, keyed by eShopOnWeb user.</summary>
    public const string CustomerReferencePrefix = "eshopweb-user:";

    /// <summary>Prefix for Maxio subscription references, keyed by user + plan.</summary>
    public const string SubscriptionReferencePrefix = "eshopweb-sub:";

    private readonly IMaxioApiClient _maxio;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IMaxioApiClient maxio, IOptions<MaxioOptions> options, ILogger<MaxioSubscriptionService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                $"Maxio product family is not configured. Set the '{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)}' configuration value (e.g. via user-secrets from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable).");
        }

        var products = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null)
            .Select(MapPlan)
            .OrderByDescending(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string userKey, string email, string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException("A subscription plan handle is required.");
        }

        var subscriptionReference = BuildSubscriptionReference(userKey, productHandle);

        // Idempotency: a repeated subscribe call for the same user + plan returns
        // the existing subscription instead of creating a second one.
        var existing = await _maxio.GetSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Subscription {Reference} already exists in Maxio (id {SubscriptionId}); returning it.", subscriptionReference, existing.Id);
            return new SubscribeResult(MapSubscription(existing, subscriptionReference), created: false);
        }

        var customer = await EnsureCustomerAsync(userKey, email, cancellationToken);
        var subscription = await _maxio.CreateSubscriptionAsync(
            new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customer.Id,
                Reference = subscriptionReference
            },
            cancellationToken);

        _logger.LogInformation("Created Maxio subscription {SubscriptionId} for customer {CustomerId} (reference {Reference}).",
            subscription.Id, customer.Id, subscriptionReference);

        return new SubscribeResult(MapSubscription(subscription, subscriptionReference), created: true);
    }

    public async Task<IReadOnlyList<UserSubscription>> ListUserSubscriptionsAsync(string userKey, string email, CancellationToken cancellationToken = default)
    {
        var customer = await _maxio.GetCustomerByReferenceAsync(BuildCustomerReference(userKey), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<UserSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Select(s => MapSubscription(s, s.Reference ?? string.Empty))
            .OrderByDescending(s => s.ActivatedAt ?? DateTime.MinValue)
            .ToList();
    }

    /// <summary>
    /// Returns the Maxio customer for the eShopOnWeb user, creating it on first
    /// use. The customer reference is the stable link, so a double-click or a
    /// retry can never create two customers.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(string userKey, string email, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(userKey);

        var existing = await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveNames(userKey, email);
        try
        {
            return await _maxio.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = reference
                },
                cancellationToken);
        }
        catch (BillingException ex) when (ex.StatusCode == 422 &&
            ex.Errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase) && e.Contains("unique", StringComparison.OrdinalIgnoreCase)))
        {
            // Lost a race with a concurrent create for the same user; fall back
            // to the customer the other request created.
            var racedCustomer = await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);
            if (racedCustomer is not null)
            {
                _logger.LogInformation("Customer {Reference} was created concurrently; reusing it.", reference);
                return racedCustomer;
            }

            throw;
        }
    }

    private static string BuildCustomerReference(string userKey) => $"{CustomerReferencePrefix}{userKey}";

    private static string BuildSubscriptionReference(string userKey, string productHandle) =>
        $"{SubscriptionReferencePrefix}{userKey}:{productHandle}";

    private static (string FirstName, string LastName) DeriveNames(string userKey, string email)
    {
        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        localPart = string.IsNullOrWhiteSpace(localPart) ? userKey : localPart;
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstName = ToPascal(parts.Length > 0 ? parts[0] : "Eshop");
        var lastName = parts.Length > 1 ? ToPascal(string.Join(" ", parts.Skip(1))) : "Web";
        return (firstName, lastName);
    }

    private static string ToPascal(string value) =>
        string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

    private static SubscriptionPlan MapPlan(MaxioProduct product) =>
        new()
        {
            ProductId = product.Id,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            HasTrial = product.TrialInterval is > 0,
            RequiresPaymentMethod = product.RequireCreditCard,
            Archived = product.ArchivedAt is not null,
            ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty
        };

    private static UserSubscription MapSubscription(MaxioSubscription subscription, string subscriptionReference)
    {
        var product = subscription.Product;
        return new UserSubscription
        {
            SubscriptionId = subscription.Id,
            State = subscription.State,
            ProductHandle = product?.Handle ?? string.Empty,
            ProductName = product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            Interval = product?.Interval ?? 0,
            IntervalUnit = product?.IntervalUnit ?? "month",
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            CustomerReference = subscription.Customer?.Reference ?? string.Empty,
            SubscriptionReference = subscriptionReference
        };
    }
}
