using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates subscription enrollment for eShopOnWeb users against the
/// Maxio Advanced Billing site.
///
/// Idempotency model (a double click must never create two customers or two
/// subscriptions):
/// - The Maxio customer reference is derived from the eShopOnWeb user id and
///   looked up before any creation, so customers are created exactly once.
/// - The Maxio subscription reference is derived from the user id + plan
///   handle; an existing subscription with that reference is returned as-is,
///   and per-user locking serializes concurrent enrollment attempts.
/// - All plan/price/state/next-billing-date facts are read back from Maxio,
///   which remains the billing system of record.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>
    /// Subscription states that represent a live or problem subscription the
    /// user is still enrolled in. End-of-life states (canceled, expired,
    /// failed_to_create) allow a fresh subscription to be created again.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "past_due", "soft_failure",
        "unpaid", "on_hold", "suspended", "trial_ended"
    };

    private const string PaymentCollectionMethod = "remittance";
    private const string DefaultIntervalUnit = "month";

    private readonly IMaxioClient _client;
    private readonly MaxioSettings _settings;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new(StringComparer.Ordinal);

    public MaxioSubscriptionService(
        IMaxioClient client,
        MaxioSettings settings,
        UserManager<ApplicationUser> userManager,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanView>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await FindConfiguredFamilyAsync(cancellationToken);
        var products = await _client.ListProductsForFamilyAsync(family.Id, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .Select(p => MapPlan(p, isDefault: false))
            .ToList();
    }

    public async Task<UserSubscriptionView> SubscribeAsync(string username, string? planHandle, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(username)
            ?? throw new InvalidOperationException($"User '{username}' does not exist.");

        var family = await FindConfiguredFamilyAsync(cancellationToken);
        var products = await _client.ListProductsForFamilyAsync(family.Id, cancellationToken);
        var product = ResolveProduct(products, planHandle)
            ?? throw new InvalidOperationException(
                $"Plan '{planHandle}' was not found in product family '{_settings.ProductFamilyHandle}'.");

        var semaphore = _userLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(user, cancellationToken);

            var subscriptionReference = SubscriptionReference(user.Id, product.Handle!);
            var existing = await _client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing is not null && LiveStates.Contains(existing.State ?? string.Empty))
            {
                _logger.LogInformation(
                    "User {UserId} is already enrolled in plan {PlanHandle} (subscription {SubscriptionId}); returning existing subscription.",
                    user.Id, product.Handle, existing.Id);
                return MapSubscription(existing);
            }

            var created = await _client.CreateSubscriptionAsync(new CreateMaxioSubscriptionRequest
            {
                ProductHandle = product.Handle!,
                CustomerId = customer.Id,
                Reference = subscriptionReference,
                PaymentCollectionMethod = PaymentCollectionMethod
            }, cancellationToken);

            _logger.LogInformation(
                "User {UserId} enrolled in plan {PlanHandle} (Maxio subscription {SubscriptionId}, customer {CustomerId}).",
                user.Id, product.Handle, created.Id, customer.Id);

            return MapSubscription(created);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<UserSubscriptionView>> ListUserSubscriptionsAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(username)
            ?? throw new InvalidOperationException($"User '{username}' does not exist.");

        var customer = await _client.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<UserSubscriptionView>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<MaxioProductFamily> FindConfiguredFamilyAsync(CancellationToken cancellationToken)
    {
        var family = await _client.FindProductFamilyByHandleAsync(_settings.ProductFamilyHandle, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Product family with handle '{_settings.ProductFamilyHandle}' does not exist on the Maxio site.");
        return family;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var existing = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveCustomerNames(user);
        var created = await _client.CreateCustomerAsync(new CreateMaxioCustomerRequest
        {
            Reference = reference,
            Email = user.Email,
            FirstName = firstName,
            LastName = lastName
        }, cancellationToken);

        _logger.LogInformation("Created Maxio customer {CustomerId} (reference {Reference}) for user {UserId}.",
            created.Id, reference, user.Id);
        return created;
    }

    private MaxioProduct ResolveProduct(IReadOnlyList<MaxioProduct> products, string? planHandle)
    {
        var preferredHandle = !string.IsNullOrWhiteSpace(planHandle)
            ? planHandle
            : _settings.DefaultPlanHandle;

        if (!string.IsNullOrWhiteSpace(preferredHandle))
        {
            return products.FirstOrDefault(p =>
                string.Equals(p.Handle, preferredHandle, StringComparison.OrdinalIgnoreCase)
                && p.ArchivedAt is null)!;
        }

        return products.FirstOrDefault(p => p.ArchivedAt is null)!;
    }

    private static string CustomerReference(string userId) => $"esw-user:{userId}";

    private static string SubscriptionReference(string userId, string planHandle) => $"esw-sub:{userId}:{planHandle}";

    private static (string FirstName, string LastName) DeriveCustomerNames(ApplicationUser user)
    {
        var localPart = (user.UserName ?? user.Email ?? "eShop Customer").Split('@')[0];
        var cleaned = new string(localPart.Where(char.IsLetterOrDigit).ToArray());
        if (cleaned.Length == 0)
        {
            return ("eShop", "Customer");
        }

        if (cleaned.Length == 1)
        {
            return (char.ToUpperInvariant(cleaned[0]).ToString(), "Customer");
        }

        var nameParts = SplitCamelOrSeparator(cleaned);
        var firstName = nameParts[0];
        var lastName = string.Join("", nameParts.Skip(1));
        if (lastName.Length == 0)
        {
            lastName = "Customer";
        }

        return (Capitalize(firstName), Capitalize(lastName));
    }

    private static string[] SplitCamelOrSeparator(string value)
    {
        var separators = new[] { '.', '-', '_' };
        var bySeparator = value.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        if (bySeparator.Length > 1)
        {
            return bySeparator;
        }

        var camelParts = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var c in value)
        {
            if (char.IsUpper(c) && current.Length > 0)
            {
                camelParts.Add(current.ToString());
                current.Clear();
            }
            current.Append(c);
        }

        if (current.Length > 0)
        {
            camelParts.Add(current.ToString());
        }

        return camelParts.ToArray();
    }

    private static string Capitalize(string value)
        => char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private static SubscriptionPlanView MapPlan(MaxioProduct product, bool isDefault)
    {
        return new SubscriptionPlanView
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents ?? 0,
            Interval = product.Interval ?? 1,
            IntervalUnit = string.IsNullOrWhiteSpace(product.IntervalUnit) ? DefaultIntervalUnit : product.IntervalUnit!,
            IsDefault = isDefault
        };
    }

    private static UserSubscriptionView MapSubscription(MaxioSubscription subscription)
    {
        var product = subscription.Product ?? new MaxioProduct();
        return new UserSubscriptionView
        {
            SubscriptionId = subscription.Id,
            State = subscription.State ?? string.Empty,
            Reference = subscription.Reference,
            PlanHandle = product.Handle ?? string.Empty,
            PlanName = product.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents ?? product.PriceInCents ?? 0,
            Interval = product.Interval ?? 1,
            IntervalUnit = string.IsNullOrWhiteSpace(product.IntervalUnit) ? DefaultIntervalUnit : product.IntervalUnit!,
            NextBillingDate = FormatDate(subscription.CurrentPeriodEndsAt),
            ActivatedAt = FormatDate(subscription.ActivatedAt),
            CreatedAt = FormatDate(subscription.CreatedAt),
            CanceledAt = FormatDate(subscription.CanceledAt),
            MaxioCustomerId = subscription.Customer?.Id ?? 0
        };
    }

    private static string? FormatDate(DateTime? date)
        => date?.ToString("o", CultureInfo.InvariantCulture);
}