using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription enrollment orchestration on top of the Maxio Billing API, with Maxio as the
/// billing system of record (no local persistence of billing state).
///
/// Idempotency model:
/// - One Maxio customer per shop user, keyed by the Maxio customer <c>reference</c> (the user id).
///   Maxio enforces reference uniqueness, and the client resolves a lost create-race by lookup.
/// - One live subscription per (user, plan): the subscription <c>reference</c> is derived
///   deterministically from that pair, existing subscriptions are checked before creating, and
///   Maxio's duplicate-prevention token guards against transport-level retries.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// Maxio subscription states that count as "currently held" for idempotency. End-of-life
    /// states (canceled, expired, trial_ended, ...) do not block a fresh subscribe.
    /// </summary>
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "trialing", "assessing", "active", "soft_failure",
        "past_due", "unpaid", "awaiting_signup", "on_hold", "suspended"
    };

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionBillingService(IMaxioClient maxioClient, IOptions<MaxioOptions> options)
    {
        _maxioClient = maxioClient;
        _options = options.Value;
        _options.Validate();
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListAllProductsAsync(cancellationToken);
        return products
            .Where(p => p.ArchivedAt == null)
            .Where(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .Select(p => new SubscriptionPlan(
                p.Id,
                p.Handle ?? p.Id.ToString(),
                p.Name,
                p.Description,
                p.PriceInCents,
                p.Interval,
                p.IntervalUnit,
                p.ProductFamily!.Handle))
            .ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            throw new ArgumentException("A user id is required to subscribe.", nameof(command));
        }

        var plan = (await ListPlansAsync(cancellationToken))
            .FirstOrDefault(p => string.Equals(p.Handle, command.PlanHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(command.PlanHandle);

        // 1. Ensure the Maxio customer exists (idempotent per user id).
        var customer = await _maxioClient.FindCustomerByReferenceAsync(command.UserId, cancellationToken);
        if (customer == null)
        {
            var (firstName, lastName) = DeriveNames(command.Email);
            customer = await _maxioClient.CreateCustomerAsync(new MaxioCreateCustomerRequest
            {
                FirstName = firstName,
                LastName = lastName,
                Email = command.Email,
                Reference = command.UserId
            }, cancellationToken);
        }

        // 2. Return the existing live subscription for this (user, plan) when there is one.
        var subscriptionReference = BuildSubscriptionReference(command.UserId, plan.Handle);
        var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = FindMatchingSubscription(existingSubscriptions, subscriptionReference, plan.Handle);
        if (existing != null)
        {
            return MapToDetails(existing);
        }

        // 3. Create the subscription. A 409 duplicate-submission response means Maxio already
        //    received an identical request; recover by re-reading the customer's subscriptions.
        MaxioSubscription created;
        try
        {
            created = await _maxioClient.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                Reference = subscriptionReference,
                // The shop signs shoppers up without capturing payment details, so enroll them on
                // invoice billing (remittance): Maxio generates an invoice instead of attempting
                // an automatic charge against a payment profile that does not exist.
                PaymentCollectionMethod = "remittance",
                UniquenessToken = BuildUniquenessToken(command.UserId, plan.Handle)
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateSubmission)
        {
            var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var recovered = FindMatchingSubscription(subscriptions, subscriptionReference, plan.Handle)
                ?? throw new MaxioApiException(ex.StatusCode, ex.ResponseBody,
                    "Maxio reported a duplicate submission, but no matching subscription could be found.");
            return MapToDetails(recovered);
        }

        return MapToDetails(created);
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var customer = await _maxioClient.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapToDetails).ToList();
    }

    private static MaxioSubscription? FindMatchingSubscription(
        IReadOnlyList<MaxioSubscription> subscriptions, string subscriptionReference, string planHandle)
    {
        return subscriptions
            .Where(s => LiveSubscriptionStates.Contains(s.State))
            .FirstOrDefault(s => string.Equals(s.Reference, subscriptionReference, StringComparison.OrdinalIgnoreCase))
            ?? subscriptions
                .Where(s => LiveSubscriptionStates.Contains(s.State))
                .FirstOrDefault(s => string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildSubscriptionReference(string userId, string planHandle)
    {
        return $"eshopweb-{userId}-{planHandle}";
    }

    /// <summary>
    /// Deterministic duplicate-prevention token for a (user, plan) signup: a genuine retry of the
    /// same logical request within Maxio's 60-minute window is rejected with 409, which the
    /// service recovers from by reading back the customer's subscriptions.
    /// </summary>
    private static string BuildUniquenessToken(string userId, string planHandle)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"eshopweb-signup:{userId}:{planHandle}"));
        return new Guid(bytes).ToString();
    }

    private static SubscriptionDetails MapToDetails(MaxioSubscription subscription)
    {
        var priceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new SubscriptionDetails(
            subscription.Id,
            subscription.Customer?.Id ?? 0,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            priceInCents,
            subscription.State,
            subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            subscription.CreatedAt);
    }

    /// <summary>
    /// Derives a first/last name pair for the Maxio customer record from the shop identity.
    /// The token only carries the username/email, so names are reconstructed from it.
    /// </summary>
    private static (string FirstName, string LastName) DeriveNames(string? email)
    {
        var localPart = email?.Split('@', 2)[0] ?? string.Empty;
        var tokens = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .Select(t => Capitalize(t))
            .ToArray();

        var first = tokens.Length > 0 ? tokens[0] : "eShop";
        var last = tokens.Length > 1 ? string.Join(" ", tokens.Skip(1)) : "Customer";
        return (first, last);
    }

    private static string Capitalize(string value)
    {
        return string.Create(value.Length, value, (span, source) =>
        {
            source.AsSpan().CopyTo(span);
            if (span.Length > 0 && char.IsLower(span[0]))
            {
                span[0] = char.ToUpperInvariant(span[0]);
            }
        });
    }
}
