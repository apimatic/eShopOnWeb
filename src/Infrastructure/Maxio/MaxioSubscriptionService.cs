using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements subscription billing against Maxio Advanced Billing. Owns the idempotency
/// guarantees: exactly one billing customer per shopper (keyed by the shopper's stable id via
/// the Maxio customer <c>reference</c>) and no duplicate active subscription per plan.
/// </summary>
public sealed class MaxioSubscriptionService : ISubscriptionService
{
    // Live states: an existing subscription in one of these is reused instead of creating a
    // duplicate. Terminal states (canceled, expired, failed_to_create, trial_ended) are not
    // here, so a shopper can re-subscribe after one ends.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "soft_failure",
        "past_due", "on_hold", "paused", "suspended", "unpaid", "awaiting_signup",
    };

    // Serializes subscribe operations per shopper so a double-click cannot race into two
    // customers or two subscriptions. Adequate for the single-instance reference deployment.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ReferenceLocks = new();

    private const string DefaultCurrency = "USD";

    private readonly IMaxioClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForProductFamilyAsync(FamilySegment, cancellationToken);

        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Select(ToPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string? planHandle,
        CancellationToken cancellationToken = default)
    {
        var resolvedHandle = ResolvePlanHandle(planHandle);

        // Validate the plan is part of the configured family (and capture its details).
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, resolvedHandle, StringComparison.OrdinalIgnoreCase))
                   ?? throw new PlanNotFoundException(resolvedHandle);

        var gate = ReferenceLocks.GetOrAdd(subscriber.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

            // Idempotency: reuse a live subscription to the same plan if one already exists.
            var existingSubs = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = existingSubs.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                LiveStates.Contains(s.State));

            if (existing is not null)
            {
                _logger.LogInformation(
                    "Reusing existing Maxio subscription {SubscriptionId} ({State}) for customer {CustomerId} on plan {Plan}.",
                    existing.Id, existing.State, customer.Id, plan.Handle);
                return new SubscribeResult(ToSubscription(existing, plan), AlreadyExisted: true);
            }

            var created = await _client.CreateSubscriptionAsync(
                new MaxioCreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    PaymentCollectionMethod = "remittance",
                },
                cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} ({State}) for customer {CustomerId} on plan {Plan}.",
                created.Id, created.State, customer.Id, plan.Handle);

            return new SubscribeResult(ToSubscription(created, plan), AlreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        var customer = await _client.LookupCustomerByReferenceAsync(subscriber.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subs = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subs.Select(s => ToSubscription(s, plan: null)).ToList();
    }

    /// <summary>
    /// Ensures a single Maxio customer exists for the shopper. Looks up by reference first;
    /// creates only if absent. Handles the race where a concurrent create already claimed the
    /// reference (Maxio enforces reference uniqueness) by re-looking-up.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(subscriber.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ResolveName(subscriber);

        try
        {
            var created = await _client.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = subscriber.Email,
                    Reference = subscriber.UserId,
                },
                cancellationToken);

            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for shopper reference {Reference}.",
                created.Id, subscriber.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Likely a lost race on the unique reference; re-resolve rather than fail.
            var recovered = await _client.LookupCustomerByReferenceAsync(subscriber.UserId, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw new SubscriptionException(
                "Could not create the billing customer.", ex.Errors);
        }
    }

    private string FamilySegment => $"handle:{_settings.ProductFamilyHandle}";

    private string ResolvePlanHandle(string? planHandle)
    {
        var handle = !string.IsNullOrWhiteSpace(planHandle) ? planHandle : _settings.DefaultPlanHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new SubscriptionException(
                "A plan handle is required. Provide 'planHandle' or configure Maxio:DefaultPlanHandle.");
        }

        return handle.Trim();
    }

    private static (string FirstName, string LastName) ResolveName(SubscriberIdentity subscriber)
    {
        var first = subscriber.FirstName;
        if (string.IsNullOrWhiteSpace(first))
        {
            var local = subscriber.Email.Split('@', 2)[0];
            first = string.IsNullOrWhiteSpace(local) ? "eShop" : local;
        }

        var last = string.IsNullOrWhiteSpace(subscriber.LastName) ? "Subscriber" : subscriber.LastName;
        return (first, last);
    }

    private static SubscriptionPlan ToPlan(MaxioProduct p)
    {
        var currency = DefaultCurrency;
        return new SubscriptionPlan(
            Handle: p.Handle!,
            Name: p.Name,
            Description: p.Description,
            PriceInCents: (int)p.PriceInCents,
            FormattedPrice: FormatPrice(p.PriceInCents, currency),
            Interval: p.Interval,
            IntervalUnit: p.IntervalUnit,
            Currency: currency,
            RequiresPaymentMethod: p.RequireCreditCard);
    }

    private static CustomerSubscription ToSubscription(MaxioSubscription s, SubscriptionPlan? plan)
    {
        var priceInCents = s.ProductPriceInCents > 0
            ? s.ProductPriceInCents
            : s.Product?.PriceInCents ?? (plan?.PriceInCents ?? 0);
        var currency = !string.IsNullOrWhiteSpace(s.Currency) ? s.Currency! : DefaultCurrency;

        return new CustomerSubscription(
            Id: s.Id,
            State: s.State,
            PlanHandle: s.Product?.Handle ?? plan?.Handle ?? string.Empty,
            PlanName: s.Product?.Name ?? plan?.Name ?? string.Empty,
            PriceInCents: (int)priceInCents,
            FormattedPrice: FormatPrice(priceInCents, currency),
            Interval: s.Product?.Interval ?? plan?.Interval ?? 0,
            IntervalUnit: s.Product?.IntervalUnit ?? plan?.IntervalUnit ?? string.Empty,
            Currency: currency,
            CurrentPeriodEndsAt: s.CurrentPeriodEndsAt,
            NextBillingAt: s.NextAssessmentAt,
            CustomerId: s.Customer?.Id ?? 0,
            CreatedAt: s.CreatedAt);
    }

    private static string FormatPrice(long cents, string currency)
    {
        var amount = cents / 100m;
        return string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)
            ? "$" + amount.ToString("0.00", CultureInfo.InvariantCulture)
            : amount.ToString("0.00", CultureInfo.InvariantCulture) + " " + currency;
    }
}
