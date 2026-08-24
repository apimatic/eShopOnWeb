using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Orchestrates the subscription billing capability against Maxio Advanced Billing,
/// which is the system of record for plans, customers and subscriptions.
/// The eShopOnWeb username is used as the Maxio customer reference, which makes
/// customer creation and subscription enrollment idempotent.
/// </summary>
public class SubscriptionBillingService
{
    // States after which a subscription no longer represents an enrollment; anything
    // else (active, trialing, past_due, on_hold, awaiting_signup, ...) counts as subscribed.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly IMaxioClient _maxio;
    private readonly MaxioSettings _settings;

    public SubscriptionBillingService(IMaxioClient maxio, IOptions<MaxioSettings> settings)
    {
        _maxio = maxio;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxio.ListProductsForProductFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null)
            .Select(MapPlan)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<(SubscriptionDto Subscription, bool AlreadyExisted)> SubscribeAsync(
        string username, string productHandle, CancellationToken cancellationToken = default)
    {
        // Guard against subscribing to plans outside the configured catalog.
        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new UnknownPlanException(productHandle);
        }

        var customer = await EnsureCustomerAsync(username, cancellationToken);

        // Idempotency: a live subscription to the same plan means this is a retry/double-click.
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            !TerminalStates.Contains(s.State ?? string.Empty));
        if (existing is not null)
        {
            return (MapSubscription(existing), true);
        }

        // "remittance" (spec: Collection-Method) issues an invoice instead of charging a card at
        // signup, so enrollment works for products configured without a required payment method.
        var created = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            ProductHandle = productHandle,
            CustomerId = customer.Id,
            Reference = $"eshop:{username}:{productHandle}",
            PaymentCollectionMethod = "remittance"
        }, cancellationToken);

        return (MapSubscription(created), false);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string username, CancellationToken cancellationToken = default)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(username, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string username, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(username, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCreateCustomer
            {
                Email = username,
                FirstName = DeriveFirstName(username),
                LastName = "Customer",
                Organization = "eShopOnWeb",
                Reference = username
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent request that created the customer first.
            customer = await _maxio.FindCustomerByReferenceAsync(username, cancellationToken);
            if (customer is not null)
            {
                return customer;
            }
            throw;
        }
    }

    private static string DeriveFirstName(string username)
    {
        var localPart = username.Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart) ? "Shopper" : localPart;
    }

    private static SubscriptionPlanDto MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty
    };

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? subscription.ProductPriceInCents,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        Currency = subscription.Currency,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        Reference = subscription.Reference
    };
}

public class UnknownPlanException : Exception
{
    public UnknownPlanException(string productHandle)
        : base($"No subscription plan with handle '{productHandle}' exists in the configured product family.")
    {
    }
}
