using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements the subscription capability with Maxio Billing as the system of record.
///
/// Identity mapping: the eShopOnWeb user id is stored as the Maxio customer
/// <c>reference</c>, so the user ↔ billing-customer link lives entirely in the
/// billing system and survives local database resets.
///
/// Idempotency:
/// - customers: lookup-by-reference before create; Maxio enforces reference uniqueness,
///   and a lost create race falls back to lookup.
/// - subscriptions: a per-user in-process lock serializes concurrent subscribes, an
///   existing live subscription to the plan short-circuits creation, and every create
///   carries a Maxio <c>uniqueness_token</c> as transport-level duplicate prevention.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserGates = new();
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "pending", "assessing", "past_due", "on_hold", "suspended",
        "trial_ended", "awaiting_signup", "unpaid",
    };

    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

    private readonly IMaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionService(IMaxioApiClient client, IOptions<MaxioOptions> options, UserManager<ApplicationUser> userManager)
    {
        _client = client;
        _options = options.Value;
        _userManager = userManager;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync()
    {
        var products = await _client.ListProductsAsync();

        return products
            .Where(p => p.ArchivedAt is null)
            .Where(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .Select(MapPlan)
            .OrderBy(p => p.Price)
            .ToList();
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(string userName, string planHandle)
    {
        var customer = await ResolveCustomerInfoAsync(userName);

        var gate = UserGates.GetOrAdd(customer.UserId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(LockTimeout))
        {
            throw new TimeoutException("Another subscribe request for this user is still in progress.");
        }
        try
        {
            var plan = (await ListPlansAsync()).FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
                ?? throw new UnknownSubscriptionPlanException(planHandle);

            var maxioCustomer = await EnsureCustomerAsync(customer);

            var existing = await FindLiveSubscriptionAsync(maxioCustomer.Id, plan.Handle);
            if (existing is not null)
            {
                return new SubscriptionEnrollment
                {
                    CreatedNew = false,
                    BillingCustomerId = maxioCustomer.Id,
                    BillingCustomerReference = maxioCustomer.Reference ?? customer.UserId,
                    Subscription = MapSubscription(existing),
                };
            }

            var created = await CreateSubscriptionSafeAsync(plan.Handle, maxioCustomer.Id);
            return new SubscriptionEnrollment
            {
                CreatedNew = true,
                BillingCustomerId = maxioCustomer.Id,
                BillingCustomerReference = maxioCustomer.Reference ?? customer.UserId,
                Subscription = MapSubscription(created),
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(string userName)
    {
        var customer = await ResolveCustomerInfoAsync(userName);
        var maxioCustomer = await EnsureCustomerAsync(customer);
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(maxioCustomer.Id);

        return subscriptions
            .Select(MapSubscription)
            .OrderByDescending(s => s.SubscriptionId)
            .ToList();
    }

    private async Task<SubscriptionCustomerInfo> ResolveCustomerInfoAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName)
            ?? throw new InvalidOperationException($"No account exists for user '{userName}'.");
        return new SubscriptionCustomerInfo(user.Id, user.UserName ?? userName, user.Email ?? userName);
    }

    private async Task<MaxioSubscription> CreateSubscriptionSafeAsync(string planHandle, int maxioCustomerId)
    {
        try
        {
            return await _client.CreateSubscriptionAsync(planHandle, maxioCustomerId, Guid.NewGuid().ToString());
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422 && IsMissingPaymentMethod(ex))
        {
            // The product does not require a card but the signup still needs a payment
            // method for automatic collection — fall back to invoice-style remittance
            // billing, which needs no card capture.
            return await _client.CreateSubscriptionAsync(
                planHandle, maxioCustomerId, Guid.NewGuid().ToString(), paymentCollectionMethod: "remittance");
        }
    }

    private static bool IsMissingPaymentMethod(MaxioApiException ex) =>
        ex.ResponseBody.Contains("payment method", StringComparison.OrdinalIgnoreCase)
        || ex.ResponseBody.Contains("payment profile", StringComparison.OrdinalIgnoreCase)
        || ex.ResponseBody.Contains("credit card", StringComparison.OrdinalIgnoreCase);

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriptionCustomerInfo customer)
    {
        var found = await _client.FindCustomerByReferenceAsync(customer.UserId);
        if (found is not null)
        {
            return found;
        }

        try
        {
            var (firstName, lastName) = SplitName(customer.UserName);
            return await _client.CreateCustomerAsync(new MaxioCreateCustomerPayload
            {
                FirstName = firstName,
                LastName = lastName,
                Email = customer.Email,
                Reference = customer.UserId,
            });
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Lost a create race on the unique reference — the customer must exist now.
            var raced = await _client.FindCustomerByReferenceAsync(customer.UserId);
            if (raced is not null)
            {
                return raced;
            }
            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int maxioCustomerId, string planHandle)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(maxioCustomerId);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
            && s.State is not null
            && LiveStates.Contains(s.State));
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product)
    {
        return new SubscriptionPlan
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            Price = FromCents(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? string.Empty,
            HasTrial = product.TrialInterval is > 0,
            RequirePaymentMethod = product.RequireCreditCard,
        };
    }

    private static SubscriptionSummary MapSubscription(MaxioSubscription subscription)
    {
        return new SubscriptionSummary
        {
            SubscriptionId = subscription.Id,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            Price = FromCents(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0),
            Interval = subscription.Product?.Interval ?? 0,
            IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
        };
    }

    private static decimal FromCents(long cents) => cents / 100m;

    private static (string FirstName, string LastName) SplitName(string userName)
    {
        var localPart = userName.Contains('@') ? userName[..userName.IndexOf('@')] : userName;
        var parts = localPart.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return ("eShop", "Shopper");
        }
        if (parts.Length == 1)
        {
            return (ToDisplayName(parts[0]), "Shopper");
        }
        return (ToDisplayName(parts[0]), ToDisplayName(string.Join(' ', parts[1..])));
    }

    private static string ToDisplayName(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
