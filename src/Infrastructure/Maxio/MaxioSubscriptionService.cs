using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates subscription signup against Maxio Advanced Billing and keeps a
/// local user ↔ subscription mapping. Idempotency contract:
/// - one Maxio customer per eShopOnWeb user (Maxio customer reference = user id)
/// - one subscription per user per plan (local unique index (UserId, ProductHandle))
/// A double-click therefore never creates a second customer or subscription.
/// </summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly IMaxioClient _maxio;
    private readonly AppIdentityDbContext _identityDb;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioClient maxio,
        AppIdentityDbContext identityDb,
        UserManager<ApplicationUser> userManager,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _maxio = maxio;
        _identityDb = identityDb;
        _userManager = userManager;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products
            .Where(p => p.ProductFamily?.Handle == _options.ProductFamilyHandle && p.ArchivedAt is null)
            .OrderBy(p => p.PriceInCents)
            .Select(ToPlan)
            .ToList();
    }

    public async Task<SubscriptionResult> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(userName)
            ?? throw new InvalidOperationException($"User '{userName}' was not found.");

        var products = await _maxio.ListProductsAsync(cancellationToken);
        var product = products.FirstOrDefault(p =>
            p.Handle == productHandle &&
            p.ProductFamily?.Handle == _options.ProductFamilyHandle &&
            p.ArchivedAt is null);

        if (product is null)
        {
            throw new MaxioPlanNotFoundException(productHandle);
        }

        // Idempotent replay: this user is already subscribed to this plan.
        var existing = await FindUserSubscriptionAsync(user.Id, productHandle, cancellationToken);
        if (existing is not null)
        {
            return await HydrateFromLiveAsync(existing, wasExisting: true, cancellationToken);
        }

        var customer = await EnsureMaxioCustomerAsync(user, cancellationToken);

        // Subscription reference is unique per subscription in Maxio: scope it to user + plan.
        var subscriptionReference = $"{user.Id}:{product.Handle}";

        MaxioSubscription subscription;
        try
        {
            subscription = await CreateSubscriptionAdaptiveAsync(product.Handle, customer.Id, subscriptionReference, cancellationToken);
        }
        catch (MaxioApiException ex) when (
            ex.StatusCode == 422 &&
            ex.Errors.Any(e => e.Contains("Reference", StringComparison.OrdinalIgnoreCase) && e.Contains("unique", StringComparison.OrdinalIgnoreCase)))
        {
            // Lost a race: a concurrent request created this subscription for us.
            var raced = await AwaitUserSubscriptionAsync(user.Id, productHandle, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation("Concurrent subscribe detected for user {UserId}, plan {ProductHandle}; returning the existing subscription.", user.Id, productHandle);
                return await HydrateFromLiveAsync(raced, wasExisting: true, cancellationToken);
            }
            throw;
        }

        var record = new UserSubscription
        {
            UserId = user.Id,
            MaxioCustomerId = customer.Id,
            MaxioSubscriptionId = subscription.Id,
            ProductHandle = product.Handle,
            ProductName = product.Name,
            PriceInCents = product.PriceInCents,
            State = subscription.State,
            NextBillingAt = subscription.NextAssessmentAt,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            _identityDb.UserSubscriptions.Add(record);
            await _identityDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race with a concurrent subscribe for the same user + plan:
            // the unique index guarantees a single row; replay the existing one.
            _logger.LogInformation("Concurrent subscribe detected for user {UserId}, plan {ProductHandle}; returning the existing subscription.", user.Id, productHandle);
            existing = await AwaitUserSubscriptionAsync(user.Id, productHandle, cancellationToken);
            if (existing is null)
            {
                throw;
            }
            return await HydrateFromLiveAsync(existing, wasExisting: true, cancellationToken);
        }

        return new SubscriptionResult
        {
            MaxioSubscriptionId = subscription.Id,
            MaxioCustomerId = customer.Id,
            ProductHandle = product.Handle,
            ProductName = product.Name,
            PriceInCents = product.PriceInCents,
            State = subscription.State,
            NextBillingAt = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt,
            WasExisting = false
        };
    }

    public async Task<IReadOnlyList<SubscriptionResult>> ListForUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(userName)
            ?? throw new InvalidOperationException($"User '{userName}' was not found.");

        var records = await _identityDb.UserSubscriptions
            .AsNoTracking()
            .Where(s => s.UserId == user.Id)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        var results = new List<SubscriptionResult>();
        foreach (var record in records)
        {
            results.Add(await HydrateFromLiveAsync(record, wasExisting: true, cancellationToken));
        }
        return results;
    }

    /// <summary>
    /// Creates the subscription with the site's default collection method ("automatic").
    /// When the site requires a payment method at signup and none is on file (cardless
    /// signup, per this site's plan configuration), retries with "remittance" — both
    /// values are the Collection-Method enum from the Maxio OpenAPI spec. This keeps
    /// cardless subscribe working regardless of the target site's payment settings.
    /// </summary>
    private async Task<MaxioSubscription> CreateSubscriptionAdaptiveAsync(string productHandle, int customerId, string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await _maxio.CreateSubscriptionAsync(productHandle, customerId, reference, paymentCollectionMethod: null, cancellationToken);
        }
        catch (MaxioApiException ex) when (
            ex.StatusCode == 422 &&
            ex.Errors.Any(e => e.Contains("payment method", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("Site requires a payment method at signup; retrying subscription {ProductHandle} with payment_collection_method=remittance.", productHandle);
            return await _maxio.CreateSubscriptionAsync(productHandle, customerId, reference, paymentCollectionMethod: "remittance", cancellationToken);
        }
    }

    private async Task<UserSubscription?> FindUserSubscriptionAsync(string userId, string productHandle, CancellationToken cancellationToken) =>
        await _identityDb.UserSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.ProductHandle == productHandle, cancellationToken);

    /// <summary>
    /// After losing a subscribe race, briefly waits for the winning request to persist
    /// its subscription row so the loser can replay it instead of failing.
    /// </summary>
    private async Task<UserSubscription?> AwaitUserSubscriptionAsync(string userId, string productHandle, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var record = await FindUserSubscriptionAsync(userId, productHandle, cancellationToken);
            if (record is not null)
            {
                return record;
            }
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the Maxio customer for the user, creating it on first use.
    /// The Maxio customer reference is the eShopOnWeb user id, which Maxio enforces
    /// as unique per site — so enrollment is idempotent even across races.
    /// </summary>
    private async Task<MaxioCustomer> EnsureMaxioCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = SplitName(user.UserName ?? user.Email ?? user.Id);
        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = user.Email ?? $"{user.Id}@eshoponweb.local",
                Reference = user.Id
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422 && ex.Errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase)))
        {
            // Another concurrent request just created the customer for this reference.
            _logger.LogInformation("Maxio customer for reference {UserId} already exists (created concurrently).", user.Id);
            return await _maxio.FindCustomerByReferenceAsync(user.Id, cancellationToken)
                ?? throw new InvalidOperationException($"Maxio rejected customer creation for reference '{user.Id}' but the customer could not be found.", ex);
        }
    }

    /// <summary>
    /// Refreshes the record's state from Maxio (billing system of record) and maps it to a result.
    /// Falls back to the stored snapshot if Maxio cannot be reached.
    /// </summary>
    private async Task<SubscriptionResult> HydrateFromLiveAsync(UserSubscription record, bool wasExisting, CancellationToken cancellationToken)
    {
        MaxioSubscription? live = null;
        try
        {
            var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(record.MaxioCustomerId, cancellationToken);
            live = subscriptions.FirstOrDefault(s => s.Id == record.MaxioSubscriptionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not refresh live state from Maxio for subscription {SubscriptionId}; using the stored snapshot.", record.MaxioSubscriptionId);
        }

        if (live is not null && (live.State != record.State || live.NextAssessmentAt != record.NextBillingAt))
        {
            record.State = live.State;
            record.NextBillingAt = live.NextAssessmentAt;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            try
            {
                _identityDb.UserSubscriptions.Update(record);
                await _identityDb.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not persist refreshed state for subscription {SubscriptionId}.", record.MaxioSubscriptionId);
            }
        }

        return new SubscriptionResult
        {
            MaxioSubscriptionId = record.MaxioSubscriptionId,
            MaxioCustomerId = record.MaxioCustomerId,
            ProductHandle = record.ProductHandle,
            ProductName = record.ProductName,
            PriceInCents = record.PriceInCents,
            State = live?.State ?? record.State,
            NextBillingAt = live?.NextAssessmentAt ?? record.NextBillingAt,
            CurrentPeriodEndsAt = live?.CurrentPeriodEndsAt,
            CreatedAt = record.CreatedAt,
            WasExisting = wasExisting
        };
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Price = (product.PriceInCents / 100m).ToString("F2"),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        TrialDays = product.TrialIntervalUnit == "day" ? product.TrialInterval : null,
        RequiresPaymentMethod = product.RequireCreditCard,
        IsActive = true
    };

    private static (string FirstName, string LastName) SplitName(string userNameOrEmail)
    {
        var localPart = userNameOrEmail.Contains('@')
            ? userNameOrEmail[..userNameOrEmail.IndexOf('@')]
            : userNameOrEmail;
        var separators = new[] { '.', '_', '-', ' ' };
        var tokens = localPart.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstName = tokens.Length > 0 ? tokens[0] : "eShop";
        var lastName = tokens.Length > 1 ? string.Join(" ", tokens[1..]) : "Shopper";
        return (firstName, lastName);
    }
}