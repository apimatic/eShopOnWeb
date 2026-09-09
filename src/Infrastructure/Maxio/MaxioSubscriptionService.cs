using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing-backed implementation of <see cref="ISubscriptionService"/>.
/// Maxio is the billing system of record: the eShopOnWeb user id is stored as the
/// Maxio customer <c>reference</c>, and each subscription carries a deterministic
/// <c>reference</c> derived from the user id and product handle. Both are unique per
/// the spec, which makes "ensure customer" and "subscribe" idempotent without any
/// local persistence.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>
    /// Serializes enroll/unsubscribe work per user so a double-click can never create
    /// two customers or two subscriptions. Cross-process races are additionally covered
    /// by Maxio's unique-reference enforcement (handled as idempotent replays below).
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IMaxioClient maxioClient, IOptions<MaxioOptions> options, ILogger<MaxioSubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await ResolveProductFamilyAsync(cancellationToken);
        var products = await _maxioClient.ListProductFamilyProductsAsync(family.Id, cancellationToken);

        return products
            .Where(p => p.ArchivedAt == null)
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Price = ToUnits(p.PriceInCents),
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty,
                RequireCreditCard = p.RequireCreditCard,
                ProductFamilyHandle = p.ProductFamily?.Handle ?? family.Handle ?? string.Empty
            })
            .OrderBy(p => p.Price)
            .ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.UserId)) throw new ArgumentException("A user id is required to subscribe.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.Email)) throw new ArgumentException("A user email is required to subscribe.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.ProductHandle)) throw new ArgumentException("A product handle is required to subscribe.", nameof(command));

        // Validate the plan up front so an unknown handle 404s before any Maxio enrollment.
        var product = await GetPlanProductAsync(command.ProductHandle, cancellationToken);

        var userLock = _userLocks.GetOrAdd(command.UserId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var subscriptionReference = $"{command.UserId}:{command.ProductHandle}";
            var existing = await _maxioClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing != null && !IsTerminal(existing.State))
            {
                _logger.LogInformation("Subscription {Reference} already exists in state {State}; returning it idempotently.", subscriptionReference, existing.State);
                return Map(existing, alreadySubscribed: true);
            }

            if (existing != null)
            {
                // A terminal (canceled/expired) subscription already owns the deterministic
                // reference; re-subscribing gets a fresh unique reference.
                subscriptionReference = $"{subscriptionReference}:{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            }

            var customer = await EnsureCustomerAsync(command, cancellationToken);

            try
            {
                var created = await _maxioClient.CreateSubscriptionAsync(customer.Id, command.ProductHandle, subscriptionReference, _options.PaymentCollectionMethod, cancellationToken);
                _logger.LogInformation("Created Maxio subscription {SubscriptionId} ({Product}) for user {UserId}.",
                    created.Id, command.ProductHandle, command.UserId);
                return Map(created, alreadySubscribed: false);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 422 && ReferenceTaken(ex))
            {
                // Lost a create race against another instance: return the winning subscription.
                var winner = await _maxioClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (winner != null)
                {
                    return Map(winner, alreadySubscribed: true);
                }
                throw;
            }
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A user id is required.", nameof(userId));

        var customer = await _maxioClient.ReadCustomerByReferenceAsync(userId, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(s => Map(s)).ToList();
    }

    public async Task<SubscriptionDetails> CancelAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A user id is required.", nameof(userId));

        var subscription = await _maxioClient.ReadSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription == null)
        {
            throw new KeyNotFoundException($"Subscription {subscriptionId} was not found.");
        }

        var ownerReference = subscription.Customer?.Reference;
        if (!string.Equals(ownerReference, userId, StringComparison.Ordinal))
        {
            _logger.LogWarning("User {UserId} attempted to cancel subscription {SubscriptionId} owned by reference {OwnerReference}.",
                userId, subscriptionId, ownerReference);
            throw new UnauthorizedAccessException("The subscription does not belong to this user.");
        }

        if (IsTerminal(subscription.State))
        {
            return Map(subscription);
        }

        var canceled = await _maxioClient.CancelSubscriptionAsync(subscriptionId, cancellationToken);
        return Map(canceled);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var customer = await _maxioClient.ReadCustomerByReferenceAsync(command.UserId, cancellationToken);
        if (customer != null)
        {
            return customer;
        }

        var (firstName, lastName) = DeriveCustomerName(command.Email);
        try
        {
            return await _maxioClient.CreateCustomerAsync(firstName, lastName, command.Email, command.UserId, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422 && ReferenceTaken(ex))
        {
            // Customer was created by a concurrent request between lookup and create.
            var racedCustomer = await _maxioClient.ReadCustomerByReferenceAsync(command.UserId, cancellationToken);
            if (racedCustomer != null)
            {
                return racedCustomer;
            }
            throw;
        }
    }

    private async Task<MaxioProductFamily> ResolveProductFamilyAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured. Set it via user-secrets or the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }

        var families = await _maxioClient.ListProductFamiliesAsync(cancellationToken);
        return families.FirstOrDefault(f => string.Equals(f.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Configured Maxio:ProductFamilyHandle '{_options.ProductFamilyHandle}' was not found on the Maxio site.");
    }

    private async Task<MaxioProduct> GetPlanProductAsync(string productHandle, CancellationToken cancellationToken)
    {
        var family = await ResolveProductFamilyAsync(cancellationToken);
        var products = await _maxioClient.ListProductFamilyProductsAsync(family.Id, cancellationToken);
        return products.FirstOrDefault(p =>
                   string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
                   p.ArchivedAt == null)
            ?? throw new PlanNotFoundException(productHandle);
    }

    private static SubscriptionDetails Map(MaxioSubscription subscription, bool alreadySubscribed = false)
    {
        var priceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new SubscriptionDetails
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = priceInCents,
            Price = ToUnits(priceInCents),
            Interval = subscription.Product?.Interval ?? 0,
            IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CurrentPeriodStart = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEnd = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt,
            Reference = subscription.Reference,
            AlreadySubscribed = alreadySubscribed
        };
    }

    private static decimal ToUnits(int cents) => cents / 100m;

    private static bool IsTerminal(string? state) =>
        string.Equals(state, "canceled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, "expired", StringComparison.OrdinalIgnoreCase);

    private static bool ReferenceTaken(MaxioApiException ex) =>
        ex.Errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase)) ||
        (ex.ResponseBody?.Contains("reference", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// Maxio customers require first/last name (spec Create-Customer schema); eShopOnWeb
    /// identity only carries an email, so derive a display name from the address.
    /// </summary>
    private static (string FirstName, string LastName) DeriveCustomerName(string email)
    {
        var localPart = email.Split('@')[0];
        var nameParts = localPart.Split(new[] { '.', '-', '_', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = Capitalize(nameParts.Length > 0 ? nameParts[0] : "eShop");
        var lastName = Capitalize(nameParts.Length > 1 ? nameParts[^1] : "Shopper");
        return (firstName, lastName);
    }

    private static string Capitalize(string value)
    {
        var letters = new string(value.TakeWhile(char.IsLetter).ToArray());
        if (letters.Length == 0)
        {
            return "eShop";
        }
        return char.ToUpperInvariant(letters[0]) + letters[1..].ToLowerInvariant();
    }
}
