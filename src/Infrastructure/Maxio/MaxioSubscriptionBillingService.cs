using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the subscription lifecycle against Maxio (Advanced Billing) while keeping
/// the local enrollment projection in sync. All decisions that matter are made against
/// Maxio (the system of record); the local row simply makes replays cheap and auditable.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Subscriptions that are still "live" from the shopper's perspective. A subscription in
    // any other state (canceled, expired, trial_ended, ...) is terminal, so subscribing again
    // legitimately creates a fresh subscription.
    private static readonly HashSet<string> TerminalSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "trial_ended",
        "failed_to_create",
    };

    // Enroll with remittance collection so that subscribing never requires capturing a
    // payment method (these plans are configured with "payment method not required").
    // Maxio issues invoices for the plan balance instead of attempting to auto-charge.
    private const string RemittanceCollectionMethod = "remittance";

    // Serializes subscribe attempts per (subscriber, plan) within this process so a
    // double-click can never drive two Maxio subscription creations.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new();

    private readonly IMaxioApiClient _maxioClient;
    private readonly CatalogContext _catalogContext;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient maxioClient,
        CatalogContext catalogContext,
        MaxioOptions options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxioClient = maxioClient;
        _catalogContext = catalogContext;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = _options.ProductFamilyHandle;
        var products = await _maxioClient.ListProductsByFamilyHandleAsync(familyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && p.Handle is not null)
            .OrderBy(p => p.PriceInCents ?? long.MaxValue)
            .Select(p => new SubscriptionPlanDto
            {
                ProductHandle = p.Handle!,
                Name = p.Name ?? p.Handle!,
                Description = p.Description,
                PriceInCents = p.PriceInCents ?? 0,
                Price = Money.FromCents(p.PriceInCents),
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? "month",
                RequiresPaymentMethod = p.RequireCreditCard,
                ProductPricePointName = p.ProductPricePointName,
                ProductPricePointHandle = p.ProductPricePointHandle,
                Archived = false,
            })
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string subscriberKey, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriberKey))
        {
            throw new ArgumentException("A subscriber key is required.", nameof(subscriberKey));
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var gate = SubscribeGates.GetOrAdd($"{subscriberKey}|{planHandle}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(subscriberKey, planHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetailsDto>> ListSubscriptionsAsync(string subscriberKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriberKey))
        {
            throw new ArgumentException("A subscriber key is required.", nameof(subscriberKey));
        }

        var customer = await _maxioClient.FindCustomerByReferenceAsync(subscriberKey, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetailsDto>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => MapSubscription(s))
            .Where(s => s is not null)
            .Cast<SubscriptionDetailsDto>()
            .ToList();
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(string subscriberKey, string planHandle, CancellationToken cancellationToken)
    {
        // Only plans from the configured product family can be subscribed to.
        var familyHandle = _options.ProductFamilyHandle;
        var products = await _maxioClient.ListProductsByFamilyHandleAsync(familyHandle, cancellationToken);
        var product = products.FirstOrDefault(p =>
            p.ArchivedAt is null && p.Handle?.Equals(planHandle, StringComparison.OrdinalIgnoreCase) == true);

        if (product is null)
        {
            throw new SubscriptionPlanNotFoundException(
                $"The plan '{planHandle}' is not an available plan in product family '{familyHandle}'.");
        }

        var customer = await EnsureCustomerAsync(subscriberKey, cancellationToken);
        _logger.LogInformation("Subscribing subscriber {SubscriberKey} (Maxio customer {CustomerId}) to plan {PlanHandle}.",
            subscriberKey, customer.Id, planHandle);

        // Reconcile against Maxio first: if this subscriber already has a live subscription
        // to this plan, return it rather than creating a duplicate (idempotent replay).
        var existing = await FindLiveSubscriptionAsync(customer.Id, planHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Subscriber {SubscriberKey} is already subscribed to {PlanHandle} via Maxio subscription {SubscriptionId}.",
                subscriberKey, planHandle, existing.Id);
            await PersistEnrollmentAsync(subscriberKey, customer, planHandle, existing, cancellationToken);
            return new SubscribeResult(MapSubscription(existing)!, created: false);
        }

        MaxioSubscription created;
        try
        {
            created = await _maxioClient.CreateSubscriptionAsync(new MaxioCreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = RemittanceCollectionMethod,
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.IsClientError)
        {
            // A concurrent request may have won the race between our check and create
            // (e.g. two app instances). If the subscription now exists, honor it.
            _logger.LogWarning(ex, "Creating subscription for {SubscriberKey} / {PlanHandle} returned HTTP {StatusCode}; reconciling.",
                subscriberKey, planHandle, ex.StatusCodeValue);
            var raced = await FindLiveSubscriptionAsync(customer.Id, planHandle, cancellationToken);
            if (raced is not null)
            {
                await PersistEnrollmentAsync(subscriberKey, customer, planHandle, raced, cancellationToken);
                return new SubscribeResult(MapSubscription(raced)!, created: false);
            }

            throw;
        }

        _logger.LogInformation("Created Maxio subscription {SubscriptionId} for subscriber {SubscriberKey} on plan {PlanHandle}.",
            created.Id, subscriberKey, planHandle);

        await PersistEnrollmentAsync(subscriberKey, customer, planHandle, created, cancellationToken);
        return new SubscribeResult(MapSubscription(created)!, created: true);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string subscriberKey, CancellationToken cancellationToken)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(subscriberKey, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var profile = SubscriberProfile.FromSubscriberKey(subscriberKey);
        try
        {
            var created = await _maxioClient.CreateCustomerAsync(new MaxioCreateCustomer
            {
                FirstName = profile.FirstName,
                LastName = profile.LastName,
                Email = profile.Email,
                Organization = "eShopOnWeb",
                Reference = subscriberKey,
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for subscriber {SubscriberKey}.", created.Id, subscriberKey);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsClientError)
        {
            // "reference" is unique in Maxio, so a 422/409 means another request (or a prior
            // run) already created the customer. Fall back to the lookup to stay idempotent.
            var existing = await _maxioClient.FindCustomerByReferenceAsync(subscriberKey, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions.FirstOrDefault(s =>
            s.Product?.Handle?.Equals(planHandle, StringComparison.OrdinalIgnoreCase) == true &&
            !TerminalSubscriptionStates.Contains(s.State ?? string.Empty));
    }

    private async Task PersistEnrollmentAsync(
        string subscriberKey,
        MaxioCustomer customer,
        string planHandle,
        MaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        var existing = _catalogContext.SubscriptionEnrollments.FirstOrDefault(e =>
            e.SubscriberKey == subscriberKey && e.ProductHandle == planHandle);

        if (existing is null)
        {
            _catalogContext.SubscriptionEnrollments.Add(new SubscriptionEnrollment(
                subscriberKey,
                planHandle,
                customer.Reference ?? subscriberKey,
                customer.Id,
                subscription.Id,
                subscription.State ?? "unknown"));
        }
        else
        {
            existing.UpdateEnrollment(customer.Id, subscription.Id, subscription.State ?? existing.State);
        }

        await _catalogContext.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionDetailsDto? MapSubscription(MaxioSubscription? subscription)
    {
        if (subscription is null)
        {
            return null;
        }

        var product = subscription.Product;
        var customer = subscription.Customer;
        long priceInCents = subscription.ProductPriceInCents ?? product?.PriceInCents ?? 0;

        return new SubscriptionDetailsDto
        {
            SubscriptionId = subscription.Id,
            State = subscription.State ?? string.Empty,
            ProductHandle = product?.Handle ?? string.Empty,
            ProductName = product?.Name ?? "Unknown plan",
            ProductDescription = product?.Description,
            ProductFamilyHandle = product?.ProductFamily?.Handle,
            PriceInCents = priceInCents,
            Price = Money.FromCents(priceInCents),
            Interval = product?.Interval ?? 1,
            IntervalUnit = product?.IntervalUnit ?? "month",
            CustomerId = customer?.Id ?? subscription.Id,
            CustomerReference = customer?.Reference,
            CustomerEmail = customer?.Email,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            CanceledAt = subscription.CanceledAt,
            ExpiresAt = subscription.ExpiresAt,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            BalanceInCents = subscription.BalanceInCents ?? 0,
            SubscriptionReference = subscription.Reference,
            Currency = subscription.Currency,
        };
    }

    private static class Money
    {
        public static decimal FromCents(long? cents) => (cents ?? 0) / 100m;
    }
}

/// <summary>
/// Deterministically derives a presentable customer profile (used only at Maxio customer
/// creation time) from the subscriber key carried by the caller's identity token.
/// </summary>
internal static class SubscriberProfile
{
    public static (string FirstName, string LastName, string Email) FromSubscriberKey(string subscriberKey)
    {
        var email = subscriberKey;
        var local = subscriberKey;
        string domain = string.Empty;

        var at = subscriberKey.IndexOf('@');
        if (at >= 0)
        {
            local = subscriberKey[..at];
            domain = subscriberKey[(at + 1)..];
        }

        var parts = local.Split(new[] { '.', '-', '_', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(TitleCase)
            .Where(p => p.Length > 0)
            .ToArray();

        string first = parts.Length > 0 ? parts[0] : "Subscriber";
        string last;
        if (parts.Length > 1)
        {
            last = string.Join(' ', parts.Skip(1));
        }
        else if (!string.IsNullOrWhiteSpace(domain))
        {
            last = TitleCase(domain.Split('.')[0]);
            if (last.Length == 0)
            {
                last = "Member";
            }
        }
        else
        {
            last = "Member";
        }

        return (first, last, email);
    }

    private static string TitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        value = value.Trim();
        return char.ToUpperInvariant(value[0]) + (value.Length > 1 ? value[1..] : string.Empty);
    }
}
